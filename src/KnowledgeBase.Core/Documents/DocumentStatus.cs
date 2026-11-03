namespace KnowledgeBase.Core.Documents;

public enum DocumentStatus
{
    /// <summary>Read and split into chunks; waiting for its embeddings.</summary>
    Queued = 0,

    /// <summary>A worker is computing its embeddings.</summary>
    Indexing = 1,

    /// <summary>Searchable.</summary>
    Ready = 2,

    /// <summary>Indexing failed too many times; <see cref="Document.Error"/> says why.</summary>
    Failed = 3,
}
