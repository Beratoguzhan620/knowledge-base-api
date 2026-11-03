using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeBase.Core.Ingestion;

/// <summary>
/// Splits Markdown at its headings and remembers where each passage sits ("Returns › Refunds"), so answers
/// can point to the right part of a long page. The text keeps its Markdown; it reads fine to people and to models.
/// </summary>
public static partial class MarkdownReader
{
    public static ExtractedText Read(string markdown)
    {
        var lines = Normalize(markdown).Split('\n');
        var start = SkipFrontMatter(lines);
        var path = new string?[6];
        string? title = null;
        var sections = new List<Section>();
        var text = new StringBuilder();
        var inFence = false;

        void Flush()
        {
            var body = text.ToString().Trim();
            if (body.Length > 0)
            {
                var heading = string.Join(" › ", path.Where(h => h is not null));
                sections.Add(new Section(heading.Length > 0 ? heading : null, null, body));
            }

            text.Clear();
        }

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            var match = inFence ? Match.Empty : HeadingLine().Match(line);
            if (match.Success)
            {
                Flush();
                var level = match.Groups[1].Length;
                var heading = match.Groups[2].Value.Trim().TrimEnd('#').Trim();
                if (level == 1 && title is null)
                {
                    title = heading;
                }

                path[level - 1] = level == 1 ? null : heading; // the title is the document's name, not part of every path
                for (var deeper = level; deeper < path.Length; deeper++)
                {
                    path[deeper] = null;
                }

                continue;
            }

            text.Append(line).Append('\n');
        }

        Flush();
        return new ExtractedText(title, sections);
    }

    internal static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", string.Empty);

    private static int SkipFrontMatter(string[] lines)
    {
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return 0;
        }

        for (var i = 1; i < Math.Min(lines.Length, 100); i++)
        {
            if (lines[i].Trim() is "---" or "...")
            {
                return i + 1;
            }
        }

        return 0;
    }

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingLine();
}

/// <summary>Plain text has no structure to keep; it is one section, split into passages by the chunker.</summary>
public static class PlainTextReader
{
    public static ExtractedText Read(string text)
    {
        var body = MarkdownReader.Normalize(text).Trim();
        return new ExtractedText(null, body.Length > 0 ? [new Section(null, null, body)] : []);
    }
}
