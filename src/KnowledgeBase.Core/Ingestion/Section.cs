namespace KnowledgeBase.Core.Ingestion;

/// <summary>A stretch of a document's text under one heading or on one PDF page.</summary>
public sealed record Section(string? Heading, int? Page, string Text);

/// <summary>What a reader got out of a file: a title, if the file has one, and its text in order.</summary>
public sealed record ExtractedText(string? Title, IReadOnlyList<Section> Sections)
{
    public int CharacterCount => Sections.Sum(s => s.Text.Length);
}

/// <summary>A passage about to become a <see cref="Documents.Chunk"/>.</summary>
public sealed record ChunkDraft(int Ordinal, string Text, string? Heading, int? Page);
