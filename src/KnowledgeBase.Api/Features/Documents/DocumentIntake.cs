using System.Security.Cryptography;
using KnowledgeBase.Api.Features.Workspaces;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Ingestion;
using KnowledgeBase.Infrastructure.Ingestion;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Features.Documents;

/// <summary>
/// Takes a file into a workspace: reads its text, splits it into chunks and stores all of it in one transaction,
/// queued for the indexer. A file that cannot be read is refused here, before anything is stored.
/// </summary>
internal sealed partial class DocumentIntake(
    KnowledgeBaseDbContext db, IOptions<DocumentLimits> limits, Indexing.IndexingSignal signal, TimeProvider clock, ILogger<DocumentIntake> logger)
{
    public async Task<Document> AddAsync(Guid workspaceId, Guid userId, string fileName, byte[] content, string? title, CancellationToken ct)
    {
        var settings = limits.Value;
        var format = DocumentFormats.FromFileName(fileName)
            ?? throw new DomainException(ErrorCodes.UnsupportedFile, "Upload Markdown (.md), text (.txt) or PDF (.pdf) files.");
        if (content.Length == 0)
        {
            throw new DomainException(ErrorCodes.UnsupportedFile, "The file is empty.");
        }

        if (content.Length > settings.MaxFileBytes)
        {
            throw new DomainException(ErrorCodes.FileTooLarge, $"A file can be at most {settings.MaxFileMegabytes} MB.");
        }

        var extracted = DocumentReader.Read(format, content);
        var drafts = Chunker.Split(extracted.Sections, settings.ChunkCharacters);
        var sha = SHA256.HashData(content);
        var now = clock.GetUtcNow();
        var document = Document.Create(
            workspaceId, title ?? extracted.Title ?? string.Empty, fileName, format, content.Length, sha,
            TextLanguage.Detect(string.Join('\n', extracted.Sections.Select(s => s.Text))), drafts.Count, extracted.CharacterCount, userId, now);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // One upload at a time per workspace, so two files cannot both squeeze into the last of its storage.
        await WorkspaceAccess.LockAsync(db, workspaceId, ct);
        if (await db.Documents.AnyAsync(d => d.WorkspaceId == workspaceId && d.Sha256 == sha, ct))
        {
            throw new DomainException(ErrorCodes.DuplicateDocument, "This file is already in the workspace.");
        }

        var used = await db.Documents.Where(d => d.WorkspaceId == workspaceId).SumAsync(d => (long?)d.SizeBytes, ct) ?? 0;
        if (used + content.Length > settings.WorkspaceStorageBytes)
        {
            throw new DomainException(ErrorCodes.StorageLimitReached,
                $"The workspace holds up to {settings.WorkspaceStorageMegabytes} MB of files. Delete some to make room.");
        }

        db.Documents.Add(document);
        db.DocumentFiles.Add(DocumentFile.For(document, content));
        db.Chunks.AddRange(drafts.Select(d => Chunk.Create(document, d)));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        signal.Notify();

        LogAdded(logger, document.Id, workspaceId, format, drafts.Count, document.Language);
        return document;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} added to workspace {WorkspaceId}: {Format}, {Chunks} chunks, {Language}")]
    private static partial void LogAdded(ILogger logger, Guid documentId, Guid workspaceId, DocumentFormat format, int chunks, string language);
}
