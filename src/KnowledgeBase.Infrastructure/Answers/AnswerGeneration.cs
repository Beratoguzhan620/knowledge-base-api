using KnowledgeBase.Core.Conversations;
using KnowledgeBase.Infrastructure.Embeddings;

namespace KnowledgeBase.Infrastructure.Answers;

/// <summary>A passage handed to the model, numbered as the answer will cite it.</summary>
public sealed record AnswerSource(int Number, Guid ChunkId, Guid DocumentId, string DocumentTitle, string? Heading, int? Page, string Text);

public sealed record HistoryTurn(MessageRole Role, string Content);

public sealed record AnswerRequest(string Question, IReadOnlyList<AnswerSource> Sources, IReadOnlyList<HistoryTurn> History);

/// <summary>Writes an answer from numbered sources, piece by piece as it is produced.</summary>
public interface IAnswerGenerator
{
    string Model { get; }

    IAsyncEnumerable<string> StreamAsync(AnswerRequest request, CancellationToken ct);
}

/// <summary>Configured under <c>Chat</c>.</summary>
public sealed class ChatOptions
{
    public const string Section = "Chat";

    public ModelProvider Provider { get; set; } = ModelProvider.Local;

    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "qwen2.5:1.5b";

    public string? ApiKey { get; set; }

    public double Temperature { get; set; } = 0.1;

    public int MaxTokens { get; set; } = 700;

    public int TimeoutSeconds { get; set; } = 180;
}
