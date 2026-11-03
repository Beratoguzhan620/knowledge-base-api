namespace KnowledgeBase.Api.Features.Conversations;

/// <summary>Configured under <c>Answers</c>.</summary>
public sealed class AnswerOptions
{
    public const string Section = "Answers";

    /// <summary>How many passages go to the model.</summary>
    public int Sources { get; set; } = 6;

    /// <summary>
    /// A passage is relevant from this cosine similarity to the question on. It depends on the embedding model and
    /// was measured on the sample questions (tests/eval): MIN_LOCAL for the built-in one, MIN_NOMIC for nomic-embed-text.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.25;

    /// <summary>A passage is also relevant when it holds this share of the question's words, like a product code asked alone.</summary>
    public double MinWordShare { get; set; } = 0.6;

    /// <summary>Earlier questions and answers the model sees, to understand follow-ups.</summary>
    public int HistoryTurns { get; set; } = 3;

    /// <summary>Questions per person per minute; each one costs a model call.</summary>
    public int QuestionsPerMinute { get; set; } = 20;
}
