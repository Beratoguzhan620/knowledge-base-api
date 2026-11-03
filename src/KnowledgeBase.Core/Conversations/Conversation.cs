namespace KnowledgeBase.Core.Conversations;

/// <summary>A member's questions and the answers to them, in one workspace. Only that member sees it.</summary>
public sealed class Conversation
{
    public const int TitleMaxLength = 120;

    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Named after its first question unless someone names it.</summary>
    public string? Title { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Conversation Start(Guid workspaceId, Guid userId, string? title, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        WorkspaceId = workspaceId,
        UserId = userId,
        Title = string.IsNullOrWhiteSpace(title) ? null : Clean(title),
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Rename(string title) => Title = Clean(title);

    /// <summary>A question was asked; the first one names a conversation that has no name yet.</summary>
    public void Asked(string question, DateTimeOffset now)
    {
        UpdatedAt = now;
        if (Title is null)
        {
            var oneLine = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Title = oneLine.Length <= 60 ? oneLine : oneLine[..59].TrimEnd() + "…";
        }
    }

    private static string Clean(string? title)
    {
        var clean = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is >= 1 and <= TitleMaxLength
            ? clean
            : throw new DomainException(ErrorCodes.ValidationFailed, $"A title has 1 to {TitleMaxLength} characters.");
    }
}
