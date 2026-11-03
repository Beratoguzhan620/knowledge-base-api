namespace KnowledgeBase.Core.Documents;

/// <summary>
/// Picks the PostgreSQL text search configuration for a document, so "returns" finds "returned" in English
/// and "iade" finds "iadesi" in Turkish. A rough count of common words is enough to tell the two apart.
/// </summary>
public static class TextLanguage
{
    public const string English = "english";
    public const string Turkish = "turkish";
    public const string Simple = "simple";

    private static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "the", "and", "of", "to", "is", "in", "that", "for", "it", "with", "as", "on", "are", "be", "this", "you", "or", "by", "your", "can",
    };

    private static readonly HashSet<string> TurkishWords = new(StringComparer.Ordinal)
    {
        "ve", "bir", "bu", "için", "ile", "da", "de", "olarak", "daha", "çok", "gibi", "ya", "ama", "her", "olan", "en", "mi", "ne", "sonra", "kadar",
    };

    public static bool IsKnown(string? language) => language is English or Turkish or Simple;

    public static string Detect(string text)
    {
        int english = 0, turkish = 0, words = 0;
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '\'', '*', '#', '-').ToLowerInvariant();
            if (word.Length == 0)
            {
                continue;
            }

            words++;
            if (EnglishWords.Contains(word))
            {
                english++;
            }
            else if (TurkishWords.Contains(word) || word.AsSpan().IndexOfAny("ğışçöü") >= 0)
            {
                turkish++;
            }

            if (words >= 5000)
            {
                break;
            }
        }

        if (words < 20)
        {
            return Simple;
        }

        return english >= turkish * 2 && english * 20 >= words ? English
            : turkish >= english * 2 && turkish * 20 >= words ? Turkish
            : Simple;
    }
}
