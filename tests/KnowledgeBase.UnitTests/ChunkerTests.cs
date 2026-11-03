using KnowledgeBase.Core.Ingestion;

namespace KnowledgeBase.UnitTests;

public sealed class ChunkerTests
{
    [Fact]
    public void Short_sections_stay_whole_and_keep_their_place()
    {
        var chunks = Chunker.Split([new Section("Shipping", null, "We ship in two days."), new Section(null, 3, "Page three text.")]);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new ChunkDraft(0, "We ship in two days.", "Shipping", null), chunks[0]);
        Assert.Equal(new ChunkDraft(1, "Page three text.", null, 3), chunks[1]);
    }

    [Fact]
    public void Paragraphs_are_gathered_up_to_the_limit_and_never_cut_in_the_middle()
    {
        var paragraphs = Enumerable.Range(1, 12).Select(i => $"Paragraph {i} " + new string('x', 180) + ".").ToArray();

        var chunks = Chunker.Split([new Section(null, null, string.Join("\n\n", paragraphs))], 600);

        Assert.All(chunks, c => Assert.True(c.Text.Length <= 600, $"{c.Text.Length} characters"));
        foreach (var paragraph in paragraphs)
        {
            Assert.Contains(chunks, c => c.Text.Contains(paragraph, StringComparison.Ordinal));
        }

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void A_passage_starts_with_the_last_sentence_of_the_one_before()
    {
        var text = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"Sentence number {i} is about returns."));

        var chunks = Chunker.Split([new Section(null, null, text)], 300);

        Assert.True(chunks.Count > 2);
        for (var i = 1; i < chunks.Count; i++)
        {
            var previousLast = chunks[i - 1].Text.Split(". ").Last().TrimEnd('.');
            Assert.StartsWith(previousLast, chunks[i].Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_very_long_word_is_cut_where_it_must_be()
    {
        var chunks = Chunker.Split([new Section(null, null, new string('a', 1000))], 300);

        Assert.Equal(1000, chunks.Sum(c => c.Text.Length));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 300));
    }

    [Fact]
    public void Empty_text_makes_no_chunks()
    {
        Assert.Empty(Chunker.Split([new Section(null, null, "  \n\n  ")]));
    }
}
