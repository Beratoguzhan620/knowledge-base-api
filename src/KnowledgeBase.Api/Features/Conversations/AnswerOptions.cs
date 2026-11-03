namespace KnowledgeBase.Api.Features.Conversations;

/// <summary>Configured under <c>Answers</c>.</summary>
public sealed class AnswerOptions
{
    public const string Section = "Answers";

    /// <summary>How many passages go to the model.</summary>
    public int Sources { get; set; } = 6;

    /// <summary>
    /// A passage is relevant from this cosine similarity to the question on. It depends on the embedding model, so it
    /// is measured, not guessed: tests/eval prints how close the right passages and the best passages for questions
    /// outside the documents are. On the samples that gave 0.18 for the built-in model and 0.64 for nomic-embed-text.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.18;

    /// <summary>
    /// A passage is also relevant when it holds this share of the question's words (after stemming), like a product
    /// code asked alone. On the samples no passage held more than 0.4 of an unrelated question's words.
    /// </summary>
    public double MinWordShare { get; set; } = 0.5;

    /// <summary>Earlier questions and answers the model sees, to understand follow-ups.</summary>
    public int HistoryTurns { get; set; } = 3;

    /// <summary>Questions per person per minute; each one costs a model call.</summary>
    public int QuestionsPerMinute { get; set; } = 20;
}
