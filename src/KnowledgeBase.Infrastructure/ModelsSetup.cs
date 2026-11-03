using KnowledgeBase.Infrastructure.Embeddings;
using KnowledgeBase.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Infrastructure;

public static class ModelsSetup
{
    /// <summary>The embedding model from configuration, and the search and indexing that use it.</summary>
    public static IServiceCollection AddKnowledgeBaseEmbeddings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.Section))
            .Validate(o => o.Provider == ModelProvider.Local || Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "Embeddings:BaseUrl must be an absolute URL, like http://localhost:11434/v1/.")
            .Validate(o => o.BatchSize is >= 1 and <= 256, "Embeddings:BatchSize must be between 1 and 256.")
            .ValidateOnStart();

        var provider = configuration.GetSection(EmbeddingOptions.Section).GetValue(nameof(EmbeddingOptions.Provider), ModelProvider.Local);
        if (provider == ModelProvider.Local)
        {
            services.AddSingleton<IEmbeddingGenerator, LocalEmbeddingGenerator>();
        }
        else
        {
            services.AddHttpClient<IEmbeddingGenerator, OpenAiEmbeddingGenerator>((sp, http) =>
                {
                    var settings = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
                    Configure(http, settings.BaseUrl!, settings.ApiKey);
                })
                .AddStandardResilienceHandler(resilience =>
                {
                    var timeout = TimeSpan.FromSeconds(configuration.GetValue("Embeddings:TimeoutSeconds", 120));
                    Slow(resilience, timeout);
                });
        }

        services.AddScoped<ChunkSearch>();
        services.AddSingleton<EmbeddingWriter>();
        return services;
    }

    internal static void Configure(HttpClient http, string baseUrl, string? apiKey)
    {
        http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline decides
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>Models on a CPU can take their time; each attempt gets <paramref name="attempt"/>, two retries at most.</summary>
    internal static void Slow(HttpStandardResilienceOptions resilience, TimeSpan attempt)
    {
        resilience.AttemptTimeout.Timeout = attempt;
        resilience.TotalRequestTimeout.Timeout = attempt * 3 + TimeSpan.FromSeconds(10);
        resilience.CircuitBreaker.SamplingDuration = attempt * 2 + TimeSpan.FromSeconds(1);
        resilience.Retry.MaxRetryAttempts = 2;
    }
}
