namespace KnowledgeBase.Core.Documents;

/// <summary>
/// A file a member added to a workspace. Its text is split into <see cref="Chunk"/>s when it is uploaded;
/// a background worker then computes their embeddings, after which the document is searchable.
/// </summary>
public sealed class Document
{
    public const int TitleMaxLength = 200;
    public const int FileNameMaxLength = 255;
    public const int ErrorMaxLength = 500;

    private Document()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public DocumentFormat Format { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 of the file, to notice the same file added twice to a workspace.</summary>
    public byte[] Sha256 { get; private set; } = [];

    /// <summary>The PostgreSQL text search configuration for its chunks: <c>english</c>, <c>turkish</c> or <c>simple</c>.</summary>
    public string Language { get; private set; } = TextLanguage.Simple;

    public int ChunkCount { get; private set; }

    public int CharacterCount { get; private set; }

    public DocumentStatus Status { get; private set; }

    public string? Error { get; private set; }

    /// <summary>How many times indexing has been tried since the document was queued.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// When a queued document may be picked up (later than now after a failed attempt), or when the lease of the
    /// worker indexing it runs out.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>The embedding model its chunks were indexed with.</summary>
    public string? EmbeddingModel { get; private set; }

    public DateTimeOffset? IndexedAt { get; private set; }

    public Guid UploadedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Document Create(
        Guid workspaceId, string title, string fileName, DocumentFormat format, long sizeBytes, byte[] sha256,
        string language, int chunkCount, int characterCount, Guid uploadedBy, DateTimeOffset now)
    {
        if (chunkCount < 1)
        {
            throw new DomainException(ErrorCodes.UnsupportedFile, "The file has no text to search. Scanned PDFs without a text layer cannot be read.");
        }

        return new Document
        {
            Id = Guid.CreateVersion7(now),
            WorkspaceId = workspaceId,
            Title = Truncate(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(), TitleMaxLength),
            FileName = Truncate(Path.GetFileName(fileName), FileNameMaxLength),
            Format = format,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            Language = TextLanguage.IsKnown(language) ? language : TextLanguage.Simple,
            ChunkCount = chunkCount,
            CharacterCount = characterCount,
            Status = DocumentStatus.Queued,
            NextAttemptAt = now,
            UploadedBy = uploadedBy,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// A worker takes the document until <paramref name="leaseUntil"/>. A worker that dies mid-way leaves it
    /// <c>Indexing</c>; once the lease runs out another worker may take it again.
    /// </summary>
    public void StartIndexing(DateTimeOffset now, DateTimeOffset leaseUntil)
    {
        var expiredLease = Status == DocumentStatus.Indexing && NextAttemptAt <= now;
        if (Status != DocumentStatus.Queued && !expiredLease)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"Only a queued document can be indexed; this one is {Status}.");
        }

        Status = DocumentStatus.Indexing;
        Attempts++;
        NextAttemptAt = leaseUntil;
    }

    public void MarkReady(string embeddingModel, DateTimeOffset now)
    {
        Status = DocumentStatus.Ready;
        EmbeddingModel = embeddingModel;
        IndexedAt = now;
        Error = null;
    }

    /// <summary>
    /// Records a failed attempt. With <paramref name="retryAt"/> the document goes back in the queue for later;
    /// without it, it stays failed until someone asks for it to be indexed again.
    /// </summary>
    public void MarkFailed(string error, DateTimeOffset? retryAt)
    {
        Error = Truncate(error, ErrorMaxLength);
        if (retryAt is { } at)
        {
            Status = DocumentStatus.Queued;
            NextAttemptAt = at;
        }
        else
        {
            Status = DocumentStatus.Failed;
        }
    }

    /// <summary>Back in the queue from the start: after a failure, or to index it again with another model.</summary>
    public void Requeue(DateTimeOffset now)
    {
        if (Status == DocumentStatus.Indexing && NextAttemptAt > now)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The document is being indexed right now; try again when it is done.");
        }

        Status = DocumentStatus.Queued;
        Attempts = 0;
        Error = null;
        NextAttemptAt = now;
    }

    public void Rename(string title)
    {
        var clean = title?.Trim() ?? string.Empty;
        Title = clean.Length is >= 1 and <= TitleMaxLength
            ? clean
            : throw new DomainException(ErrorCodes.ValidationFailed, $"A title has 1 to {TitleMaxLength} characters.");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
