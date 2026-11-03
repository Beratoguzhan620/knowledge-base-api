using KnowledgeBase.Core;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Infrastructure.Embeddings;
using KnowledgeBase.Infrastructure.Persistence;
using KnowledgeBase.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Features.Indexing;

/// <summary>
/// Computes the embeddings of queued documents in the background. Workers claim one document at a time with
/// <c>FOR UPDATE SKIP LOCKED</c>, so several workers, in one process or many, never take the same document.
/// A claim is a lease: a worker that dies leaves the document to be taken over when the lease runs out. A failed
/// attempt is retried later with a growing pause, until <see cref="IndexerOptions.MaxAttempts"/>.
/// </summary>
internal sealed partial class DocumentIndexer(
    IServiceScopeFactory scopes, IndexingSignal signal, IOptions<IndexerOptions> options, IOptions<EmbeddingOptions> embeddingOptions,
    TimeProvider clock, ILogger<DocumentIndexer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.Workers).Select(_ => RunWorkerAsync(stoppingToken)));

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        var idle = TimeSpan.FromSeconds(options.Value.PollSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await IndexNextAsync(workspaceId: null, ct))
                {
                    await signal.WaitAsync(idle, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database itself is unreachable; wait and try again rather than spin.
                LogWorkerError(logger, ex);
                try
                {
                    await Task.Delay(idle, clock, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Indexes the next document due, if there is one. Tests call it directly, optionally for one workspace.</summary>
    /// <returns>Whether there was a document to work on.</returns>
    public async Task<bool> IndexNextAsync(Guid? workspaceId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>();
        var document = await ClaimAsync(db, workspaceId, ct);
        if (document is null)
        {
            return false;
        }

        var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var writer = scope.ServiceProvider.GetRequiredService<EmbeddingWriter>();
        var started = clock.GetTimestamp();
        try
        {
            var chunks = await db.Chunks.AsNoTracking()
                .Where(c => c.DocumentId == document.Id)
                .OrderBy(c => c.Ordinal)
                .Select(c => new { c.Id, c.Heading, c.Text })
                .ToListAsync(ct);

            foreach (var batch in chunks.Chunk(Math.Max(1, embeddingOptions.Value.BatchSize)))
            {
                // The title and heading go with the passage: "Refunds" alone means more under "Returns policy".
                var texts = batch.Select(c => string.Join('\n', new[] { document.Title, c.Heading }.Where(t => !string.IsNullOrEmpty(t))) + "\n\n" + c.Text).ToList();
                var vectors = await embeddings.EmbedAsync(texts, EmbeddingPurpose.Document, ct);
                await writer.WriteAsync(batch.Select((c, i) => (c.Id, vectors[i])).ToList(), ct);
            }

            document.MarkReady(embeddings.Model, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            LogIndexed(logger, document.Id, chunks.Count, embeddings.Model, clock.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Shutting down: the lease runs out and another worker finishes the job.
        }
        catch (DbUpdateConcurrencyException)
        {
            LogDeleted(logger, document.Id); // Deleted while it was being indexed; nothing left to do.
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(db, document, ex, ct);
        }

        return true;
    }

    private async Task<Document?> ClaimAsync(KnowledgeBaseDbContext db, Guid? workspaceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var due = db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM documents
            WHERE status IN ('Queued', 'Indexing') AND next_attempt_at <= {now}
            ORDER BY next_attempt_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """);
        var dueInWorkspace = db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM documents
            WHERE status IN ('Queued', 'Indexing') AND next_attempt_at <= {now} AND workspace_id = {workspaceId.GetValueOrDefault()}
            ORDER BY next_attempt_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """);
        var ids = await (workspaceId is null ? due : dueInWorkspace).ToListAsync(ct);
        if (ids.Count == 0)
        {
            return null;
        }

        var document = await db.Documents.SingleAsync(d => d.Id == ids[0], ct);
        document.StartIndexing(now, now.AddMinutes(options.Value.LeaseMinutes));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return document;
    }

    private async Task RecordFailureAsync(KnowledgeBaseDbContext db, Document document, Exception ex, CancellationToken ct)
    {
        // Only messages written for people are shown; anything else stays in the log.
        var reason = ex is DomainException or InvalidOperationException ? ex.Message : "Indexing failed unexpectedly; the server log has the details.";
        var giveUp = document.Attempts >= options.Value.MaxAttempts;
        var retryAt = giveUp ? (DateTimeOffset?)null : clock.GetUtcNow().Add(Backoff(document.Attempts));
        LogFailed(logger, ex, document.Id, document.Attempts, retryAt);

        db.ChangeTracker.Clear();
        var fresh = await db.Documents.SingleOrDefaultAsync(d => d.Id == document.Id, ct);
        if (fresh is null)
        {
            return;
        }

        fresh.MarkFailed(reason, retryAt);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>10 s, 20 s, 40 s… up to 10 minutes.</summary>
    internal static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(600, 10 * Math.Pow(2, Math.Max(0, attempts - 1))));

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} indexed: {Chunks} chunks with {Model} in {Milliseconds:F0} ms")]
    private static partial void LogIndexed(ILogger logger, Guid documentId, int chunks, string model, double milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexing document {DocumentId} failed (attempt {Attempts}); retry at {RetryAt}")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid documentId, int attempts, DateTimeOffset? retryAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} was deleted while it was being indexed")]
    private static partial void LogDeleted(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The indexer could not look for work")]
    private static partial void LogWorkerError(ILogger logger, Exception exception);
}
