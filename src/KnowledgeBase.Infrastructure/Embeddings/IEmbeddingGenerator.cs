namespace KnowledgeBase.Infrastructure.Embeddings;

/// <summary>Whether a text is a passage to store or a question to search with; some models embed them differently.</summary>
public enum EmbeddingPurpose
{
    Document,
    Query,
}

/// <summary>Turns texts into vectors of <see cref="EmbeddingOptions.Dimensions"/> numbers, close together when the meanings are.</summary>
public interface IEmbeddingGenerator
{
    /// <summary>Stored with every indexed document, so a change of model can be noticed and the documents indexed again.</summary>
    string Model { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct);
}
