namespace KnowledgeBase.Api.Features.Indexing;

/// <summary>Configured under <c>Indexer</c>.</summary>
public sealed class IndexerOptions
{
    public const string Section = "Indexer";

    /// <summary>Documents indexed at the same time by this process; 0 turns the background indexer off.</summary>
    public int Workers { get; set; } = 2;

    /// <summary>Attempts before a document is marked failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>How long a worker may hold a document before another one may take it over.</summary>
    public int LeaseMinutes { get; set; } = 10;

    /// <summary>How often idle workers look for work they were not told about (from another process, or a retry coming due).</summary>
    public int PollSeconds { get; set; } = 5;
}
