namespace KnowledgeBase.Core.Conversations;

public enum MessageRole
{
    User,
    Assistant,
}

public enum AnswerOutcome
{
    /// <summary>Answered from the workspace's documents.</summary>
    Answered,

    /// <summary>Nothing in the documents was close enough; the model was not asked.</summary>
    NotFound,

    /// <summary>The model could not be reached or stopped half-way.</summary>
    Failed,
}

/// <summary>A passage an answer points to with its number, as it was when the answer was given.</summary>
public sealed class Citation
{
    public const int QuoteMaxLength = 400;

    public int Number { get; init; }

    public Guid ChunkId { get; init; }

    public Guid DocumentId { get; init; }

    public string DocumentTitle { get; init; } = string.Empty;

    public string? Heading { get; init; }

    public int? Page { get; init; }

    /// <summary>The start of the passage, enough to recognise it even after the document is deleted.</summary>
    public string Quote { get; init; } = string.Empty;
}

public sealed class Message
{
    public const int QuestionMaxLength = 2000;

    private Message()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public MessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public AnswerOutcome? Outcome { get; private set; }

    /// <summary>The model that wrote the answer.</summary>
    public string? Model { get; private set; }

    public List<Citation> Citations { get; private set; } = [];

    /// <summary>The asker's verdict on an answer, if they gave one.</summary>
    public bool? Helpful { get; private set; }

    public int? DurationMs { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Message Question(Guid conversationId, string question, DateTimeOffset now)
    {
        var text = question?.Trim() ?? string.Empty;
        if (text.Length is < 1 or > QuestionMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A question has 1 to {QuestionMaxLength} characters.");
        }

        return new Message { Id = Guid.CreateVersion7(now), ConversationId = conversationId, Role = MessageRole.User, Content = text, CreatedAt = now };
    }

    public static Message Answer(
        Guid conversationId, string content, AnswerOutcome outcome, string? model, IEnumerable<Citation> citations, int durationMs, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        ConversationId = conversationId,
        Role = MessageRole.Assistant,
        Content = content.Trim(),
        Outcome = outcome,
        Model = model,
        Citations = citations.OrderBy(c => c.Number).ToList(),
        DurationMs = durationMs,
        CreatedAt = now,
    };

    public void RateHelpful(bool? helpful)
    {
        if (Role != MessageRole.Assistant)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only answers can be rated.");
        }

        Helpful = helpful;
    }
}
