using KnowledgeBase.Infrastructure.Embeddings;

namespace KnowledgeBase.UnitTests;

public sealed class LocalEmbeddingTests
{
    private static float Cosine(string a, string b)
    {
        var (x, y) = (LocalEmbeddingGenerator.Embed(a), LocalEmbeddingGenerator.Embed(b));
        return x.Zip(y, (p, q) => p * q).Sum();
    }

    [Fact]
    public void Vectors_have_the_column_s_size_unit_length_and_are_the_same_every_time()
    {
        var first = LocalEmbeddingGenerator.Embed("Refunds reach the card in three days.");
        var again = LocalEmbeddingGenerator.Embed("Refunds reach the card in three days.");

        Assert.Equal(EmbeddingOptions.Dimensions, first.Length);
        Assert.Equal(1.0, Math.Sqrt(first.Sum(v => v * v)), 3);
        Assert.Equal(first, again);
    }

    [Fact]
    public void Texts_about_the_same_thing_are_closer_than_unrelated_ones()
    {
        const string passage = "Refunds are paid back to the original card within three working days.";

        Assert.True(Cosine("when is my refund paid back", passage) > Cosine("when is my refund paid back", "The grinder has thirty settings for coarseness."));
        Assert.True(Cosine("iadem ne zaman yatar", "İadeniz üç iş günü içinde kartınıza yatar.") > 0.2);
    }

    [Fact]
    public void Turkish_letters_and_case_do_not_matter_and_word_endings_still_match()
    {
        Assert.Equal(LocalEmbeddingGenerator.Embed("ÖDEME"), LocalEmbeddingGenerator.Embed("odeme"));
        Assert.True(Cosine("iade", "iadesi") > Cosine("iade", "kargo"));
    }

    [Fact]
    public void Text_with_only_common_words_has_no_direction()
    {
        Assert.All(LocalEmbeddingGenerator.Embed("what is the"), v => Assert.Equal(0f, v));
    }
}
