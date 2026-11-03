using System.Security.Claims;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Api.Features.Workspaces;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Features.Documents;

public sealed record DocumentResponse(
    Guid Id, string Title, string FileName, DocumentFormat Format, long SizeBytes, string Language, DocumentStatus Status,
    string? Error, int ChunkCount, int CharacterCount, string? EmbeddingModel, string UploadedBy, DateTimeOffset CreatedAt, DateTimeOffset? IndexedAt);

public sealed record DocumentPage(IReadOnlyList<DocumentResponse> Items, int Total, int Page, int PageSize);

public sealed record ChunkResponse(Guid Id, int Ordinal, string? Heading, int? Page, string Text);

public sealed record ChunkPage(IReadOnlyList<ChunkResponse> Items, int Total, int Page, int PageSize);

public sealed record RenameDocumentRequest(string Title);

public sealed record UsageResponse(
    long StorageBytes, long StorageLimitBytes, long MaxFileBytes, int Documents, int Chunks, int Queued, int Indexing, int Ready, int Failed);

internal static class DocumentEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/{workspaceId:guid}").WithTags("Documents").RequireAuthorization();
        group.MapPost("/documents", Upload).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(64L * 1024 * 1024))
            .Accepts<IFormFile>("multipart/form-data")
            .WithSummary("Add a Markdown, text or PDF file (editors)")
            .WithDescription("Send the file as `file` in a multipart form, optionally with a `title`. The text is read and split at once; the document becomes searchable when the indexer has embedded it (`status` goes from `Queued` to `Ready`).");
        group.MapGet("/documents", List).WithSummary("Documents in the workspace")
            .WithDescription("Newest first. `q` searches titles and file names; `status` filters.");
        group.MapGet("/documents/{documentId:guid}", Get).WithSummary("One document");
        group.MapGet("/documents/{documentId:guid}/chunks", Chunks).WithSummary("The passages a document was split into");
        group.MapGet("/documents/{documentId:guid}/file", Download).WithSummary("Download the original file");
        group.MapPatch("/documents/{documentId:guid}", Rename).WithSummary("Rename a document (editors)");
        group.MapPost("/documents/{documentId:guid}/reindex", Reindex).WithSummary("Index a document again (editors)")
            .WithDescription("After a failure, or after the embedding model changed.");
        group.MapDelete("/documents/{documentId:guid}", Delete).WithSummary("Delete a document (editors)");
        group.MapGet("/usage", Usage).WithTags("Workspaces").WithSummary("Storage and indexing in the workspace");
        return app;
    }

    private static async Task<Results<Accepted<DocumentResponse>, ValidationProblem>> Upload(
        Guid workspaceId, IFormFile? file, [FromForm] string? title, ClaimsPrincipal user, KnowledgeBaseDbContext db,
        DocumentIntake intake, IOptions<DocumentLimits> limits, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Editor, ct);
        var validation = new Validation()
            .Require(file is not null, "file", "Attach the file as 'file'.")
            .Require(title is null || title.Trim().Length is >= 1 and <= Document.TitleMaxLength, "title", $"A title has 1 to {Document.TitleMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        if (file!.Length > limits.Value.MaxFileBytes)
        {
            throw new DomainException(ErrorCodes.FileTooLarge, $"A file can be at most {limits.Value.MaxFileMegabytes} MB.");
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var document = await intake.AddAsync(workspaceId, user.UserId(), file.FileName, buffer.ToArray(), title?.Trim(), ct);
        return TypedResults.Accepted($"/api/workspaces/{workspaceId}/documents/{document.Id}",
            await Project(db, db.Documents.Where(d => d.Id == document.Id)).SingleAsync(ct));
    }

    private static async Task<Results<Ok<DocumentPage>, ValidationProblem>> List(
        Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct,
        string? q = null, DocumentStatus? status = null, int page = 1, int pageSize = 50)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var validation = new Validation()
            .Require(page >= 1, "page", "Pages start at 1.")
            .Require(pageSize is >= 1 and <= MaxPageSize, "pageSize", $"A page has 1 to {MaxPageSize} documents.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.Documents.AsNoTracking().Where(d => d.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(d => EF.Functions.ILike(d.Title, pattern, "\\") || EF.Functions.ILike(d.FileName, pattern, "\\"));
        }

        if (status is { } wanted)
        {
            query = query.Where(d => d.Status == wanted);
        }

        var total = await query.CountAsync(ct);
        var items = await (from d in query
                           join u in db.Users on d.UploadedBy equals u.Id
                           orderby d.CreatedAt descending
                           select new DocumentResponse(
                               d.Id, d.Title, d.FileName, d.Format, d.SizeBytes, d.Language, d.Status, d.Error, d.ChunkCount, d.CharacterCount,
                               d.EmbeddingModel, u.DisplayName, d.CreatedAt, d.IndexedAt))
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return TypedResults.Ok(new DocumentPage(items, total, page, pageSize));
    }

    private static async Task<Ok<DocumentResponse>> Get(
        Guid workspaceId, Guid documentId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        return TypedResults.Ok(await Project(db, db.Documents.Where(d => d.Id == documentId && d.WorkspaceId == workspaceId)).SingleOrDefaultAsync(ct) ?? throw NotFound());
    }

    private static async Task<Results<Ok<ChunkPage>, ValidationProblem>> Chunks(
        Guid workspaceId, Guid documentId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var validation = new Validation()
            .Require(page >= 1, "page", "Pages start at 1.")
            .Require(pageSize is >= 1 and <= MaxPageSize, "pageSize", $"A page has 1 to {MaxPageSize} passages.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct)
            ?? throw NotFound();
        var items = await db.Chunks.AsNoTracking()
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.Ordinal)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new ChunkResponse(c.Id, c.Ordinal, c.Heading, c.Page, c.Text))
            .ToListAsync(ct);
        return TypedResults.Ok(new ChunkPage(items, document.ChunkCount, page, pageSize));
    }

    private static async Task<FileContentHttpResult> Download(
        Guid workspaceId, Guid documentId, ClaimsPrincipal user, KnowledgeBaseDbContext db, HttpContext http, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var found = await (from d in db.Documents
                           join f in db.DocumentFiles on d.Id equals f.DocumentId
                           where d.Id == documentId && d.WorkspaceId == workspaceId
                           select new { d.FileName, d.Format, f.Content }).SingleOrDefaultAsync(ct)
            ?? throw NotFound();

        // Always a download, never shown inline: an uploaded file is data, not a page of this site.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(found.Content, DocumentFormats.ContentType(found.Format), found.FileName);
    }

    private static async Task<Ok<DocumentResponse>> Rename(
        Guid workspaceId, Guid documentId, RenameDocumentRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Editor, ct);
        var document = await Find(db, workspaceId, documentId, ct);
        document.Rename(request.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await Project(db, db.Documents.Where(d => d.Id == documentId)).SingleAsync(ct));
    }

    private static async Task<Accepted<DocumentResponse>> Reindex(
        Guid workspaceId, Guid documentId, ClaimsPrincipal user, KnowledgeBaseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Editor, ct);
        var document = await Find(db, workspaceId, documentId, ct);
        document.Requeue(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted($"/api/workspaces/{workspaceId}/documents/{documentId}",
            await Project(db, db.Documents.Where(d => d.Id == documentId)).SingleAsync(ct));
    }

    private static async Task<NoContent> Delete(
        Guid workspaceId, Guid documentId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Editor, ct);
        var deleted = await db.Documents.Where(d => d.Id == documentId && d.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? throw NotFound() : TypedResults.NoContent();
    }

    private static async Task<Ok<UsageResponse>> Usage(
        Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, IOptions<DocumentLimits> limits, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var byStatus = await db.Documents.Where(d => d.WorkspaceId == workspaceId)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Bytes = g.Sum(d => d.SizeBytes), Chunks = g.Sum(d => d.ChunkCount) })
            .ToListAsync(ct);
        int Count(DocumentStatus s) => byStatus.Where(g => g.Status == s).Sum(g => g.Count);
        return TypedResults.Ok(new UsageResponse(
            byStatus.Sum(g => g.Bytes), limits.Value.WorkspaceStorageBytes, limits.Value.MaxFileBytes,
            byStatus.Sum(g => g.Count), byStatus.Sum(g => g.Chunks),
            Count(DocumentStatus.Queued), Count(DocumentStatus.Indexing), Count(DocumentStatus.Ready), Count(DocumentStatus.Failed)));
    }

    private static async Task<Document> Find(KnowledgeBaseDbContext db, Guid workspaceId, Guid documentId, CancellationToken ct) =>
        await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct) ?? throw NotFound();

    private static DomainException NotFound() => new(ErrorCodes.NotFound, "There is no document with this id in the workspace.");

    private static IQueryable<DocumentResponse> Project(KnowledgeBaseDbContext db, IQueryable<Document> documents) =>
        from d in documents.AsNoTracking()
        join u in db.Users on d.UploadedBy equals u.Id
        select new DocumentResponse(
            d.Id, d.Title, d.FileName, d.Format, d.SizeBytes, d.Language, d.Status, d.Error, d.ChunkCount, d.CharacterCount,
            d.EmbeddingModel, u.DisplayName, d.CreatedAt, d.IndexedAt);
}
