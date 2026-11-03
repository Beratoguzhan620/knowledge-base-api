using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Api.Features.Workspaces;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Conversations;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Features.Conversations;

public sealed record StartConversationRequest(string? Title);

public sealed record RenameConversationRequest(string Title);

public sealed record AskRequest(string Question);

public sealed record FeedbackRequest(bool? Helpful);

public sealed record CitationResponse(int Number, Guid ChunkId, Guid DocumentId, string DocumentTitle, string? Heading, int? Page, string Quote);

public sealed record MessageResponse(
    Guid Id, MessageRole Role, string Content, AnswerOutcome? Outcome, string? Model, IReadOnlyList<CitationResponse> Citations,
    bool? Helpful, int? DurationMs, DateTimeOffset CreatedAt)
{
    public static MessageResponse From(Message m) => new(
        m.Id, m.Role, m.Content, m.Outcome, m.Model,
        m.Citations.Select(c => new CitationResponse(c.Number, c.ChunkId, c.DocumentId, c.DocumentTitle, c.Heading, c.Page, c.Quote)).ToList(),
        m.Helpful, m.DurationMs, m.CreatedAt);
}

public sealed record ConversationResponse(Guid Id, string? Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ConversationDetailResponse(Guid Id, string? Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<MessageResponse> Messages);

public sealed record AnswerResponse(IReadOnlyList<SourceResponse> Sources, MessageResponse Question, MessageResponse Answer);

internal static class ConversationEndpoints
{
    public const string RateLimit = "questions";

    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/{workspaceId:guid}/conversations").WithTags("Questions").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Your conversations in the workspace, latest first")
            .WithDescription("Conversations are private: other members, owners included, do not see them.");
        group.MapPost("/", Start).WithSummary("Start a conversation");
        group.MapGet("/{conversationId:guid}", Get).WithSummary("A conversation with its questions and answers");
        group.MapPatch("/{conversationId:guid}", Rename).WithSummary("Rename a conversation");
        group.MapDelete("/{conversationId:guid}", Delete).WithSummary("Delete a conversation");
        group.MapPost("/{conversationId:guid}/messages", Ask).RequireRateLimiting(RateLimit)
            .Produces<AnswerResponse>().Produces(StatusCodes.Status200OK, contentType: "text/event-stream").ProducesValidationProblem()
            .WithSummary("Ask a question")
            .WithDescription("""
                Answered only from the workspace's documents, with numbered citations like [1]. When nothing relevant is
                found the answer says so, and no model is asked (`outcome` is `NotFound`).

                With `Accept: text/event-stream` the answer streams as server-sent events: `sources` (the passages, numbered),
                `delta` (pieces of text) and `done` (the stored question and answer). Otherwise the whole answer comes back at once.
                """);
        group.MapPut("/{conversationId:guid}/messages/{messageId:guid}/feedback", Feedback)
            .WithSummary("Say whether an answer helped").WithDescription("`helpful` is `true`, `false`, or `null` to take it back.");
        return app;
    }

    private static async Task<Ok<List<ConversationResponse>>> List(Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var userId = user.UserId();
        return TypedResults.Ok(await db.Conversations.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId && c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(200)
            .Select(c => new ConversationResponse(c.Id, c.Title, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct));
    }

    private static async Task<Created<ConversationResponse>> Start(
        Guid workspaceId, StartConversationRequest? request, ClaimsPrincipal user, KnowledgeBaseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var conversation = Conversation.Start(workspaceId, user.UserId(), request?.Title, clock.GetUtcNow());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/workspaces/{workspaceId}/conversations/{conversation.Id}",
            new ConversationResponse(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt));
    }

    private static async Task<Ok<ConversationDetailResponse>> Get(
        Guid workspaceId, Guid conversationId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        var conversation = await FindAsync(db, workspaceId, conversationId, user, ct);
        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(new ConversationDetailResponse(
            conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, messages.Select(MessageResponse.From).ToList()));
    }

    private static async Task<Ok<ConversationResponse>> Rename(
        Guid workspaceId, Guid conversationId, RenameConversationRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        var conversation = await FindAsync(db, workspaceId, conversationId, user, ct);
        conversation.Rename(request.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new ConversationResponse(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt));
    }

    private static async Task<NoContent> Delete(
        Guid workspaceId, Guid conversationId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        var conversation = await FindAsync(db, workspaceId, conversationId, user, ct);
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Ask(
        Guid workspaceId, Guid conversationId, AskRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db,
        AnswerService answers, HttpContext http, IOptions<JsonOptions> json, CancellationToken ct)
    {
        var conversation = await FindAsync(db, workspaceId, conversationId, user, ct);
        var question = request.Question?.Trim() ?? string.Empty;
        var validation = new Validation()
            .Require(question.Length is >= 1 and <= Message.QuestionMaxLength, "question", $"A question has 1 to {Message.QuestionMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        if (!http.Request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SourceResponse> sources = [];
            AnswerEvent.Done? done = null;
            await answers.AnswerAsync(conversation, question, e =>
            {
                switch (e)
                {
                    case AnswerEvent.Sources s:
                        sources = s.Items;
                        break;
                    case AnswerEvent.Done d:
                        done = d;
                        break;
                }

                return ValueTask.CompletedTask;
            }, ct);
            return TypedResults.Ok(new AnswerResponse(sources, done!.Question, done.Answer));
        }

        return TypedResults.ServerSentEvents(Stream(conversation, question, answers, json.Value.SerializerOptions, ct));
    }

    /// <summary>Runs the answer and hands its events to the response as they come.</summary>
    private static async IAsyncEnumerable<SseItem<string>> Stream(
        Conversation conversation, string question, AnswerService answers, JsonSerializerOptions json, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<SseItem<string>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var work = Task.Run(async () =>
        {
            try
            {
                await answers.AnswerAsync(conversation, question, async e =>
                {
                    var item = e switch
                    {
                        AnswerEvent.Sources s => new SseItem<string>(JsonSerializer.Serialize(s.Items, json), "sources"),
                        AnswerEvent.Delta d => new SseItem<string>(JsonSerializer.Serialize(new { text = d.Text }, json), "delta"),
                        AnswerEvent.Done d => new SseItem<string>(JsonSerializer.Serialize(new { question = d.Question, answer = d.Answer }, json), "done"),
                        _ => throw new InvalidOperationException("Unknown answer event."),
                    };
                    await channel.Writer.WriteAsync(item, ct);
                }, ct);
                channel.Writer.Complete();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The response has started; the error can only travel as an event.
                var detail = ex is DomainException domain ? domain.Message : "Something went wrong while answering. Try again.";
                channel.Writer.TryWrite(new SseItem<string>(JsonSerializer.Serialize(new { detail }, json), "error"));
                channel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.Complete();
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct))
            {
                yield return item;
            }
        }
        finally
        {
            // Never leave the answer running after the request is over: it uses the request's database context.
            await work;
        }
    }

    private static async Task<Ok<MessageResponse>> Feedback(
        Guid workspaceId, Guid conversationId, Guid messageId, FeedbackRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        var conversation = await FindAsync(db, workspaceId, conversationId, user, ct);
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversation.Id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such message in the conversation.");
        message.RateHelpful(request.Helpful);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MessageResponse.From(message));
    }

    /// <summary>The conversation, if it is the caller's and they are still a member of its workspace.</summary>
    private static async Task<Conversation> FindAsync(
        KnowledgeBaseDbContext db, Guid workspaceId, Guid conversationId, ClaimsPrincipal user, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var userId = user.UserId();
        return await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId && c.WorkspaceId == workspaceId && c.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no conversation with this id among yours in the workspace.");
    }
}
