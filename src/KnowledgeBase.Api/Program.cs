using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Api.Features.Conversations;
using KnowledgeBase.Api.Features.Auth;
using KnowledgeBase.Api.Features.Documents;
using KnowledgeBase.Api.Features.Indexing;
using KnowledgeBase.Api.Features.Search;
using KnowledgeBase.Api.Features.Workspaces;
using KnowledgeBase.Api.Logging;
using KnowledgeBase.Infrastructure;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("KnowledgeBase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:KnowledgeBase is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__KnowledgeBase environment variable.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKnowledgeBasePersistence(connectionString);
builder.Services.AddKnowledgeBaseAuth(builder.Configuration);
builder.Services.AddOptions<DocumentLimits>()
    .Bind(builder.Configuration.GetSection(DocumentLimits.Section))
    .Validate(l => l.MaxFileMegabytes is >= 1 and <= 50, "Limits:MaxFileMegabytes must be between 1 and 50.")
    .Validate(l => l.WorkspaceStorageMegabytes >= l.MaxFileMegabytes, "Limits:WorkspaceStorageMegabytes must be at least Limits:MaxFileMegabytes.")
    .Validate(l => l.ChunkCharacters is >= 300 and <= 4000, "Limits:ChunkCharacters must be between 300 and 4000.")
    .ValidateOnStart();
builder.Services.AddScoped<DocumentIntake>();
builder.Services.AddKnowledgeBaseEmbeddings(builder.Configuration);
builder.Services.AddOptions<IndexerOptions>()
    .Bind(builder.Configuration.GetSection(IndexerOptions.Section))
    .Validate(o => o.Workers is >= 0 and <= 16, "Indexer:Workers must be between 0 and 16.")
    .Validate(o => o.MaxAttempts is >= 1 and <= 20, "Indexer:MaxAttempts must be between 1 and 20.")
    .Validate(o => o.LeaseMinutes >= 1 && o.PollSeconds >= 1, "Indexer:LeaseMinutes and Indexer:PollSeconds must be at least 1.")
    .ValidateOnStart();
builder.Services.AddSingleton<IndexingSignal>();
builder.Services.AddKnowledgeBaseAnswers(builder.Configuration);
builder.Services.AddOptions<AnswerOptions>()
    .Bind(builder.Configuration.GetSection(AnswerOptions.Section))
    .Validate(o => o.Sources is >= 1 and <= 20, "Answers:Sources must be between 1 and 20.")
    .Validate(o => o.MinSimilarity is >= -1 and <= 1, "Answers:MinSimilarity must be between -1 and 1.")
    .Validate(o => o.MinWordShare is > 0 and <= 1, "Answers:MinWordShare must be above 0 and at most 1.")
    .Validate(o => o.HistoryTurns is >= 0 and <= 10, "Answers:HistoryTurns must be between 0 and 10.")
    .ValidateOnStart();
builder.Services.AddScoped<AnswerService>();
builder.Services.AddSingleton<DocumentIndexer>();
builder.Services.AddHostedService(services => services.GetRequiredService<DocumentIndexer>());
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = 64L * 1024 * 1024);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-up and sign-in per address: plenty for a person, slow for guessing.
    var perMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
    options.AddPolicy(AuthEndpoints.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Questions per person: each one costs a model call.
    var questionsPerMinute = builder.Configuration.GetValue("Answers:QuestionsPerMinute", 20);
    options.AddPolicy(ConversationEndpoints.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = questionsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await TypedResults.Problem(
            title: "Too many requests",
            detail: "Slow down and try again in a minute.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = KnowledgeBase.Core.ErrorCodes.RateLimited })
            .ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<KnowledgeBaseDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Knowledge Base")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());

app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapMemberEndpoints();
app.MapDocumentEndpoints();
app.MapSearchEndpoints();
app.MapConversationEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DependencyInjection.MigrateAsync(app.Services);
}

await app.RunAsync();

public partial class Program;
