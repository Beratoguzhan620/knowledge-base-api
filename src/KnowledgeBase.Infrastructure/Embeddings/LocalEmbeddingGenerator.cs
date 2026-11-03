using System.Text;

namespace KnowledgeBase.Infrastructure.Embeddings;

/// <summary>
/// An embedding that needs no model: every word, and the three-letter pieces of every word, is hashed into one of
/// <see cref="EmbeddingOptions.Dimensions"/> slots. Texts that share words or word stems ("iade", "iadesi") point the
/// same way. It knows nothing of synonyms, so a real model finds more; but it is fast, free, the same on every
/// machine, and lets the whole system run and be tested without downloading anything.
/// </summary>
public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator
{
    public const string ModelName = "local-hashing-v1";

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "do", "for", "from", "how", "i", "in", "is", "it", "my", "of", "on",
        "or", "the", "this", "to", "we", "what", "when", "where", "which", "who", "why", "with", "you", "your",
        "acaba", "ama", "bir", "bu", "da", "de", "en", "gibi", "hangi", "icin", "ile", "mi", "mu", "nasil", "ne", "neden", "nedir",
        "olan", "olarak", "ve", "veya", "ya",
    };

    public string Model => ModelName;

    internal static bool IsStopWord(string word) => StopWords.Contains(word);

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct) =>
        Task.FromResult(texts.Select(Embed).ToArray());

    public static float[] Embed(string text)
    {
        var vector = new float[EmbeddingOptions.Dimensions];
        foreach (var word in Words(text))
        {
            if (StopWords.Contains(word))
            {
                continue;
            }

            Add(vector, "w:" + word, 1f);
            var padded = $"<{word}>";
            if (padded.Length > 4)
            {
                var weight = 1f / (padded.Length - 2);
                for (var i = 0; i + 3 <= padded.Length; i++)
                {
                    Add(vector, "t:" + padded.Substring(i, 3), weight);
                }
            }
        }

        var length = MathF.Sqrt(vector.Sum(x => x * x));
        if (length > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= length;
            }
        }

        return vector;
    }

    /// <summary>Lower case, with Turkish letters folded to their plain forms so "Ödeme" and "odeme" meet.</summary>
    internal static IEnumerable<string> Words(string text)
    {
        var word = new StringBuilder();
        foreach (var raw in text)
        {
            var c = Fold(raw);
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    private static char Fold(char c) => c switch
    {
        'İ' or 'I' or 'ı' => 'i',
        'Ğ' or 'ğ' => 'g',
        'Ş' or 'ş' => 's',
        'Ç' or 'ç' => 'c',
        'Ö' or 'ö' => 'o',
        'Ü' or 'ü' => 'u',
        _ => char.ToLowerInvariant(c),
    };

    private static void Add(float[] vector, string feature, float weight)
    {
        // FNV-1a; the top bit picks a sign, so unrelated features cancel out instead of piling up.
        var hash = 14695981039346656037UL;
        foreach (var c in feature)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        var slot = (int)(hash % (ulong)vector.Length);
        vector[slot] += (hash >> 63) == 0 ? weight : -weight;
    }
}
