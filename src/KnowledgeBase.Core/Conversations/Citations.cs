using System.Text.RegularExpressions;

namespace KnowledgeBase.Core.Conversations;

/// <summary>Reads the source numbers an answer cites, written like [1] or [2, 3].</summary>
public static partial class Citations
{
    /// <returns>The numbers from 1 to <paramref name="sources"/>, each once, in the order they first appear.</returns>
    public static IReadOnlyList<int> Used(string answer, int sources)
    {
        var used = new List<int>();
        foreach (Match match in Bracket().Matches(answer))
        {
            foreach (var part in match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var number) && number >= 1 && number <= sources && !used.Contains(number))
                {
                    used.Add(number);
                }
            }
        }

        return used;
    }

    [GeneratedRegex(@"\[(\d{1,2}(?:\s*,\s*\d{1,2})*)\]")]
    private static partial Regex Bracket();
}

/// <summary>Short fixed phrases, in the language of the question: Turkish if it looks Turkish, English otherwise.</summary>
public static class Phrases
{
    public static bool IsTurkish(string text) =>
        text.AsSpan().IndexOfAny("ğışçöüĞİŞÇÖÜ") >= 0
        || text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(w => w.Trim('?', '.', ',', '!').ToLowerInvariant() is "ne" or "nasıl" or "nedir" or "mi" or "mı" or "hangi" or "kaç" or "neden" or "nerede" or "ve" or "bir");

    public static string NotFound(string question) => IsTurkish(question)
        ? "Bu çalışma alanındaki belgelerde bu soruya cevap bulamadım. Soruyu başka kelimelerle sormayı ya da ilgili belgeyi eklemeyi deneyin."
        : "I could not find an answer to this in the workspace's documents. Try asking with other words, or add the document that covers it.";

    public static string Failed(string question) => IsTurkish(question)
        ? "Cevap yazılırken dil modeline ulaşılamadı. Biraz sonra tekrar deneyin."
        : "The language model could not be reached while writing the answer. Try again in a moment.";
}
