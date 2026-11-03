namespace KnowledgeBase.Core.Documents;

/// <summary>
/// A passage of a document, the unit that is searched and cited. Its embedding lives in a pgvector column
/// next to it and is written by the indexer; the text search vector is computed by PostgreSQL.
/// </summary>
public sealed class Chunk
{
    public const int HeadingMaxLength = 500;

    private Chunk()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>Copied from the document so every search can filter on it without a join.</summary>
    public Guid WorkspaceId { get; private set; }

    /// <summary>Position in the document, from 0.</summary>
    public int Ordinal { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>The headings the passage sits under, like "Returns › Refunds".</summary>
    public string? Heading { get; private set; }

    /// <summary>The PDF page it comes from, from 1.</summary>
    public int? Page { get; private set; }

    /// <summary>The text search configuration, the same as the document's.</summary>
    public string Language { get; private set; } = TextLanguage.Simple;

    public static Chunk Create(Document document, Ingestion.ChunkDraft draft) => new()
    {
        Id = Guid.CreateVersion7(document.CreatedAt),
        DocumentId = document.Id,
        WorkspaceId = document.WorkspaceId,
        Ordinal = draft.Ordinal,
        Text = draft.Text,
        Heading = draft.Heading is { Length: > HeadingMaxLength } heading ? heading[..HeadingMaxLength] : draft.Heading,
        Page = draft.Page,
        Language = document.Language,
    };
}
