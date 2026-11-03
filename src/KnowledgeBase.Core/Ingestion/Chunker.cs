using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeBase.Core.Ingestion;

/// <summary>
/// Cuts sections into passages small enough to embed and to quote, large enough to make sense alone.
/// Passages follow paragraphs, then sentences, and never cross a heading or a page, so every passage
/// can be cited with one place. Each one starts with the last sentence of the one before it, so a fact
/// split across the boundary is still found.
/// </summary>
public static partial class Chunker
{
    public const int DefaultMaxCharacters = 1200;
    private const int OverlapMaxCharacters = 200;

    public static IReadOnlyList<ChunkDraft> Split(IEnumerable<Section> sections, int maxCharacters = DefaultMaxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 200);
        var drafts = new List<ChunkDraft>();
        foreach (var section in sections)
        {
            foreach (var text in SplitSection(section.Text, maxCharacters))
            {
                drafts.Add(new ChunkDraft(drafts.Count, text, section.Heading, section.Page));
            }
        }

        return drafts;
    }

    private static IEnumerable<string> SplitSection(string text, int max)
    {
        var current = new StringBuilder();
        string? lastSentence = null;
        foreach (var (piece, endsParagraph) in Pieces(text, max))
        {
            if (current.Length > 0 && current.Length + piece.Length + 2 > max)
            {
                var done = current.ToString().Trim();
                yield return done;
                current.Clear();
                lastSentence = LastSentence(done);
                if (lastSentence is not null && lastSentence.Length + piece.Length + 2 <= max)
                {
                    current.Append(lastSentence).Append(' ');
                }
            }

            // Sentences of one paragraph stay on one line; paragraphs keep their blank line.
            current.Append(piece).Append(endsParagraph ? "\n\n" : " ");
        }

        var rest = current.ToString().Trim();
        if (rest.Length > 0 && rest != lastSentence)
        {
            yield return rest;
        }
    }

    /// <summary>
    /// Paragraphs, with any paragraph longer than <paramref name="max"/> broken into sentences, then words.
    /// The flag marks the last piece of a paragraph.
    /// </summary>
    private static IEnumerable<(string Text, bool EndsParagraph)> Pieces(string text, int max)
    {
        foreach (var paragraph in Paragraphs().Split(text))
        {
            var clean = paragraph.Trim();
            if (clean.Length == 0)
            {
                continue;
            }

            if (clean.Length <= max)
            {
                yield return (clean, true);
                continue;
            }

            var pieces = Sentences().Split(clean)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .SelectMany(s => s.Length <= max ? (IEnumerable<string>)[s] : Words(s, max))
                .ToList();
            for (var i = 0; i < pieces.Count; i++)
            {
                yield return (pieces[i], i == pieces.Count - 1);
            }
        }
    }

    private static IEnumerable<string> Words(string text, int max)
    {
        var slice = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (slice.Length > 0 && slice.Length + word.Length + 1 > max)
            {
                yield return slice.ToString();
                slice.Clear();
            }

            // A single "word" longer than a passage (a URL, a hash) is cut where it must be.
            for (var w = word; w.Length > 0;)
            {
                var take = Math.Min(w.Length, max - slice.Length - (slice.Length > 0 ? 1 : 0));
                if (take <= 0)
                {
                    yield return slice.ToString();
                    slice.Clear();
                    continue;
                }

                if (slice.Length > 0)
                {
                    slice.Append(' ');
                }

                slice.Append(w[..take]);
                w = w[take..];
            }
        }

        if (slice.Length > 0)
        {
            yield return slice.ToString();
        }
    }

    private static string? LastSentence(string text)
    {
        var sentences = Sentences().Split(text.Replace('\n', ' '));
        var last = sentences.LastOrDefault(s => s.Trim().Length > 0)?.Trim();
        return last is { Length: > 0 and <= OverlapMaxCharacters } && last.Length < text.Length ? last : null;
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex Paragraphs();

    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[\p{Lu}\d""'(\[])")]
    private static partial Regex Sentences();
}
