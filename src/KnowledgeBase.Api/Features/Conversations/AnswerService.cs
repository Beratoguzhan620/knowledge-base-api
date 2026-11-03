using System.Diagnostics;
using System.Text;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Conversations;
using KnowledgeBase.Infrastructure.Answers;
using KnowledgeBase.Infrastructure.Persistence;
using KnowledgeBase.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Features.Conversations;

public sealed record SourceResponse(int Number, Guid ChunkId, Guid DocumentId, string DocumentTitle, string? Heading, int? Page, string Quote);

/// <summary>What the answer stream sends, in order: the sources, the text in pieces, then the stored answer.</summary>
public abstract record AnswerEvent
{
    public sealed record Sources(IReadOnlyList<SourceResponse> Items) : AnswerEvent;

    public sealed record Delta(string Text) : AnswerEvent;

    public sealed record Done(MessageResponse Question, MessageResponse Answer) : AnswerEvent;
}

/// <summary>
/// Answers a question from a workspace's documents: finds the passages, decides whether they are close enough
/// to answer from, has the model write the answer citing them, and stores both question and answer. When nothing
/// relevant is found the model is not asked at all: an answer that is not in the documents is worse than none.
/// </summary>
internal sealed partial class AnswerService(
    KnowledgeBaseDbContext db, ChunkSearch search, IAnswerGenerator generator, IOptions<AnswerOptions> options,
    TimeProvider clock, ILogger<AnswerService> logger)
{
    public async Task AnswerAsync(Conversation conversation, string question, Func<AnswerEvent, ValueTask> send, CancellationToken ct)
    {
        var settings = options.Value;
        var started = Stopwatch.GetTimestamp();
        var now = clock.GetUtcNow();
        var asked = Message.Question(conversation.Id, question, now);
        var history = await HistoryAsync(conversation.Id, settings.HistoryTurns, ct);

        // The question alone decides what is relevant: mixing in the previous one would let an unrelated question
        // borrow its passages and be "answered". The model still sees the earlier turns for follow-ups.
        var hits = await search.SearchAsync(conversation.WorkspaceId, asked.Content, settings.Sources, SearchMode.Hybrid, ct);
        var relevant = hits.Where(h => h.KeywordRank is not null || h.Similarity >= settings.MinSimilarity).ToList();
        var sources = relevant.Select((h, i) => new AnswerSource(i + 1, h.ChunkId, h.DocumentId, h.DocumentTitle, h.Heading, h.Page, h.Text)).ToList();
        await send(new AnswerEvent.Sources(sources.Select(ToResponse).ToList()));

        string text;
        AnswerOutcome outcome;
        string? model = null;
        if (sources.Count == 0)
        {
            text = Phrases.NotFound(asked.Content);
            outcome = AnswerOutcome.NotFound;
            await send(new AnswerEvent.Delta(text));
        }
        else
        {
            var written = new StringBuilder();
            model = generator.Model;
            try
            {
                await foreach (var piece in generator.StreamAsync(new AnswerRequest(asked.Content, sources, history), ct))
                {
                    written.Append(piece);
                    await send(new AnswerEvent.Delta(piece));
                }

                // The markers may be missing (small models forget them); then the server attributes the sentences.
                text = CitationRepair.Attribute(written.ToString(), sources);
                outcome = AnswerOutcome.Answered;
            }
            catch (Exception ex) when (ex is DomainException { Code: ErrorCodes.ProviderUnavailable } or HttpRequestException or IOException && !ct.IsCancellationRequested)
            {
                LogModelFailed(logger, ex, conversation.WorkspaceId);
                var failed = Phrases.Failed(asked.Content);
                await send(new AnswerEvent.Delta((written.Length > 0 ? "\n\n" : string.Empty) + failed));
                text = written.Length > 0 ? written + "\n\n" + failed : failed;
                outcome = AnswerOutcome.Failed;
            }
        }

        // Only the passages the answer points to are kept with it.
        var cited = Citations.Used(text, sources.Count);
        var citations = sources.Where(s => cited.Contains(s.Number)).Select(s => new Citation
        {
            Number = s.Number,
            ChunkId = s.ChunkId,
            DocumentId = s.DocumentId,
            DocumentTitle = s.DocumentTitle,
            Heading = s.Heading,
            Page = s.Page,
            Quote = Quote(s.Text),
        });
        var elapsed = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var answer = Message.Answer(conversation.Id, text, outcome, model, citations, elapsed, clock.GetUtcNow());

        // The answer is stored even if the asker has gone: CancellationToken.None.
        conversation.Asked(asked.Content, now);
        db.Messages.AddRange(asked, answer);
        await db.SaveChangesAsync(CancellationToken.None);
        LogAnswered(logger, conversation.WorkspaceId, outcome, sources.Count, answer.Citations.Count, elapsed);
        await send(new AnswerEvent.Done(MessageResponse.From(asked), MessageResponse.From(answer)));
    }

    private async Task<List<HistoryTurn>> HistoryAsync(Guid conversationId, int turns, CancellationToken ct)
    {
        var recent = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(turns * 2)
            .Select(m => new { m.Role, m.Content, m.Outcome })
            .ToListAsync(ct);
        recent.Reverse();
        // Failed answers teach the model nothing; leave them out.
        return recent.Where(m => m.Outcome != AnswerOutcome.Failed).Select(m => new HistoryTurn(m.Role, m.Content)).ToList();
    }

    internal static SourceResponse ToResponse(AnswerSource s) =>
        new(s.Number, s.ChunkId, s.DocumentId, s.DocumentTitle, s.Heading, s.Page, Quote(s.Text));

    private static string Quote(string text) =>
        text.Length <= Citation.QuoteMaxLength ? text : text[..Citation.QuoteMaxLength].TrimEnd() + "…";

    [LoggerMessage(Level = LogLevel.Information, Message = "Question in workspace {WorkspaceId}: {Outcome} from {Sources} sources, {Cited} cited, in {Milliseconds} ms")]
    private static partial void LogAnswered(ILogger logger, Guid workspaceId, AnswerOutcome outcome, int sources, int cited, int milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The chat model failed while answering in workspace {WorkspaceId}")]
    private static partial void LogModelFailed(ILogger logger, Exception exception, Guid workspaceId);
}
