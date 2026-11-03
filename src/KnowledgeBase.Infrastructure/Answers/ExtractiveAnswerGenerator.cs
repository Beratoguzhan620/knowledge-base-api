using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KnowledgeBase.Core.Conversations;
using KnowledgeBase.Infrastructure.Embeddings;

namespace KnowledgeBase.Infrastructure.Answers;

/// <summary>
/// Answers without a language model: it quotes the sentences of the sources that share the most words with the
/// question, each with its source number. It cannot rephrase or combine facts, but it never says anything the
/// documents do not, and it lets the whole system run offline and in tests.
/// </summary>
public sealed partial class ExtractiveAnswerGenerator : IAnswerGenerator
{
    public const string ModelName = "local-extractive-v1";
    private const int MaxSentences = 3;

    public string Model => ModelName;

    public async IAsyncEnumerable<string> StreamAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var questionWords = Overlaps.ContentWords(request.Question);
        var ranked = request.Sources
            .SelectMany(source => Sentences().Split(source.Text.Replace('\n', ' '))
                .Select(sentence => sentence.Trim().TrimStart('#', '-', '*', ' '))
                .Where(sentence => sentence.Length >= 15)
                .Select((sentence, position) => (source.Number, Sentence: sentence, Position: position,
                    Score: Overlaps.Score(questionWords, sentence) - (0.01 * source.Number))))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ToList();

        // Sentences well behind the best one are left out: a quote that only shares a word with the question is noise.
        var best = ranked
            .Where(s => s.Score >= ranked[0].Score * 0.6)
            .Take(MaxSentences)
            .OrderBy(s => s.Number).ThenBy(s => s.Position)
            .ToList();

        if (best.Count == 0)
        {
            best = request.Sources.Take(1)
                .Select(source => (source.Number, Sentence: source.Text.Split('\n')[0].Trim(), Position: 0, Score: 0.0))
                .ToList();
        }

        yield return Phrases.IsTurkish(request.Question) ? "Belgelerde şöyle geçiyor: " : "The documents say: ";
        foreach (var (number, sentence, _, _) in best)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            var text = sentence.TrimEnd();
            yield return (text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text[..^1] : text) + $" [{number}]. ";
        }
    }

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex Sentences();
}
