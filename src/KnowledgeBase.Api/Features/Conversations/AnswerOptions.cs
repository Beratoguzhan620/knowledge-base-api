namespace KnowledgeBase.Api.Features.Conversations;

/// <summary>Configured under <c>Answers</c>.</summary>
public sealed class AnswerOptions
{
    public const string Section = "Answers";

    /// <summary>How many passages go to the model.</summary>
    public int Sources { get; set; } = 6;

    /// <summary>
    /// A passage found only by meaning counts as relevant from this cosine similarity on; one that also shares a
    /// word with the question always does. Depends on the embedding model: about 0.25 for the built-in one,
    /// about 0.5 for nomic-embed-text.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.25;

    /// <summary>Earlier questions and answers the model sees, to understand follow-ups.</summary>
    public int HistoryTurns { get; set; } = 3;

    /// <summary>Questions per person per minute; each one costs a model call.</summary>
    public int QuestionsPerMinute { get; set; } = 20;
}
