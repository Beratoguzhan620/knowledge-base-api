using System.Security.Claims;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Api.Features.Indexing;
using KnowledgeBase.Api.Features.Workspaces;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Embeddings;
using KnowledgeBase.Infrastructure.Persistence;
using KnowledgeBase.Infrastructure.Search;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Features.Search;

public sealed record SearchHitResponse(
    Guid ChunkId, Guid DocumentId, string DocumentTitle, int Ordinal, string? Heading, int? Page, string Text,
    double Score, int? VectorRank, int? KeywordRank, double? Similarity, double WordShare);

public sealed record SearchResponse(string Query, SearchMode Mode, string EmbeddingModel, IReadOnlyList<SearchHitResponse> Hits);

public sealed record ReindexResponse(int Queued);

internal static class SearchEndpoints
{
    public const int QueryMaxLength = 500;
    private const int MaxLimit = 50;

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/{workspaceId:guid}").RequireAuthorization();
        group.MapGet("/search", Search).WithTags("Search").WithSummary("Find the passages that match a question")
            .WithDescription("`mode` is `Hybrid` (meaning and words, the default), `Vector` or `Keyword`. Each hit says where it ranked in each search.");
        group.MapPost("/reindex", Reindex).WithTags("Documents").WithSummary("Index the workspace again (owners)")
            .WithDescription("Queues the documents indexed with another embedding model than the current one; with `all=true`, every document.");
        return app;
    }

    private static async Task<Results<Ok<SearchResponse>, ValidationProblem>> Search(
        Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, ChunkSearch search, IEmbeddingGenerator embeddings,
        CancellationToken ct, string? q = null, int limit = 8, SearchMode mode = SearchMode.Hybrid)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var query = q?.Trim() ?? string.Empty;
        var validation = new Validation()
            .Require(query.Length is >= 1 and <= QueryMaxLength, "q", $"Ask with 1 to {QueryMaxLength} characters.")
            .Require(limit is >= 1 and <= MaxLimit, "limit", $"Ask for 1 to {MaxLimit} passages.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var hits = await search.SearchAsync(workspaceId, query, limit, mode, ct);
        return TypedResults.Ok(new SearchResponse(query, mode, embeddings.Model, hits.Select(h => new SearchHitResponse(
            h.ChunkId, h.DocumentId, h.DocumentTitle, h.Ordinal, h.Heading, h.Page, h.Text, h.Score, h.VectorRank, h.KeywordRank, h.Similarity, h.WordShare)).ToList()));
    }

    private static async Task<Accepted<ReindexResponse>> Reindex(
        Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, IEmbeddingGenerator embeddings, IndexingSignal signal,
        TimeProvider clock, CancellationToken ct, bool all = false)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Owner, ct);
        var model = embeddings.Model;
        var documents = await db.Documents
            .Where(d => d.WorkspaceId == workspaceId && (d.Status == DocumentStatus.Ready || d.Status == DocumentStatus.Failed))
            .Where(d => all || d.EmbeddingModel != model || d.Status == DocumentStatus.Failed)
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var document in documents)
        {
            document.Requeue(now);
        }

        await db.SaveChangesAsync(ct);
        signal.Notify();
        return TypedResults.Accepted($"/api/workspaces/{workspaceId}/documents?status=Queued", new ReindexResponse(documents.Count));
    }
}
