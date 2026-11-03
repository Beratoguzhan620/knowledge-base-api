using KnowledgeBase.Core.Conversations;
using KnowledgeBase.Infrastructure.Embeddings;

namespace KnowledgeBase.Infrastructure.Answers;

/// <summary>How much two texts share, by their content words; word endings are forgiven ("refund", "refunds").</summary>
internal static class Overlaps
{
    public static HashSet<string> ContentWords(string text) =>
        LocalEmbeddingGenerator.Words(text).Where(w => w.Length > 2 && !LocalEmbeddingGenerator.IsStopWord(w)).ToHashSet();

    /// <summary>Shared content words, scaled down for long sentences.</summary>
    public static double Score(HashSet<string> words, string sentence)
    {
        var other = ContentWords(sentence);
        var shared = other.Count(w => words.Any(q => Same(q, w)));
        return shared == 0 ? 0 : shared / Math.Sqrt(other.Count + 1);
    }

    public static int Shared(HashSet<string> words, string text) => ContentWords(text).Count(w => words.Any(q => Same(q, w)));

    private static bool Same(string a, string b) =>
        a == b || (a.Length >= 4 && b.Length >= 4 && string.CompareOrdinal(a, 0, b, 0, Math.Min(5, Math.Min(a.Length, b.Length))) == 0);
}

/// <summary>
/// Small models sometimes answer correctly but forget the [n] markers. Then each sentence of the answer is
/// attributed to the source it shares the most content words with, if it shares at least two, so the answer can
/// still be traced to the documents. Sentences that match no source stay unmarked.
/// </summary>
public static class CitationRepair
{
    public static string Attribute(string answer, IReadOnlyList<AnswerSource> sources)
    {
        if (sources.Count == 0 || Citations.Used(answer, sources.Count).Count > 0)
        {
            return answer;
        }

        var sourceWords = sources.Select(s => (s.Number, Words: Overlaps.ContentWords(s.Text))).ToList();
        var sentences = System.Text.RegularExpressions.Regex.Split(answer.Trim(), @"(?<=[.!?])\s+");
        var marked = sentences.Select(sentence =>
        {
            var words = Overlaps.ContentWords(sentence);
            var best = sourceWords
                .Select(s => (s.Number, Shared: Overlaps.Shared(s.Words, sentence)))
                .Where(s => s.Shared >= 2 && words.Count > 0)
                .OrderByDescending(s => s.Shared).ThenBy(s => s.Number)
                .FirstOrDefault();
            if (best.Number == 0)
            {
                return sentence;
            }

            var end = sentence.Length > 0 && ".!?".Contains(sentence[^1], StringComparison.Ordinal) ? sentence[^1].ToString() : string.Empty;
            return $"{sentence[..^end.Length]} [{best.Number}]{end}";
        });
        return string.Join(' ', marked);
    }
}
