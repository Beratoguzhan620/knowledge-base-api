namespace KnowledgeBase.Infrastructure.Embeddings;

public enum ModelProvider
{
    /// <summary>Built in, no model to download: hashed words and word pieces. Good for tests and trying things out.</summary>
    Local,

    /// <summary>Any service with the OpenAI API shape: Ollama, LM Studio, vLLM or OpenAI itself.</summary>
    OpenAI,
}

/// <summary>Configured under <c>Embeddings</c>.</summary>
public sealed class EmbeddingOptions
{
    public const string Section = "Embeddings";

    /// <summary>The size of the <c>embedding</c> column. Every model used must produce vectors of this size.</summary>
    public const int Dimensions = 768;

    public ModelProvider Provider { get; set; } = ModelProvider.Local;

    /// <summary>The API's address up to and including <c>/v1/</c>, like <c>http://ollama:11434/v1/</c>.</summary>
    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>From user secrets or the environment; not needed by local services.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Ask the service for <see cref="Dimensions"/> numbers (OpenAI's text-embedding-3 models shorten their vectors on request).</summary>
    public bool RequestDimensions { get; set; }

    /// <summary>Put in front of passages and questions; nomic-embed-text expects <c>search_document: </c> and <c>search_query: </c>.</summary>
    public string DocumentPrefix { get; set; } = string.Empty;

    public string QueryPrefix { get; set; } = string.Empty;

    /// <summary>How many passages go to the service in one request.</summary>
    public int BatchSize { get; set; } = 32;

    public int TimeoutSeconds { get; set; } = 120;
}
