using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Ingestion;

namespace KnowledgeBase.UnitTests;

public sealed class MarkdownReaderTests
{
    [Fact]
    public void Headings_become_the_path_of_each_section_and_the_first_one_is_the_title()
    {
        const string markdown = """
            ---
            author: someone
            ---
            # Returns policy

            Intro text.

            ## Refunds

            Money back in 14 days.

            ### Cards

            Card refunds take 3 days.

            ## Exchanges

            Swap within a month.
            """;

        var result = MarkdownReader.Read(markdown);

        Assert.Equal("Returns policy", result.Title);
        Assert.Equal(
            new (string?, string)[] { (null, "Intro text."), ("Refunds", "Money back in 14 days."), ("Refunds › Cards", "Card refunds take 3 days."), ("Exchanges", "Swap within a month.") },
            result.Sections.Select(s => (s.Heading, s.Text)).ToArray());
    }

    [Fact]
    public void Hash_signs_inside_code_blocks_are_not_headings()
    {
        var result = MarkdownReader.Read("## Setup\n\n```bash\n# install\nnpm install\n```\n");

        Assert.Single(result.Sections);
        Assert.Contains("# install", result.Sections[0].Text, StringComparison.Ordinal);
        Assert.Equal("Setup", result.Sections[0].Heading);
    }

    [Fact]
    public void Windows_line_endings_are_read_like_any_other()
    {
        var result = MarkdownReader.Read("# Title\r\n\r\nLine one\r\n## Part\r\nLine two");

        Assert.Equal("Title", result.Title);
        Assert.Equal(["Line one", "Line two"], result.Sections.Select(s => s.Text));
    }

    [Theory]
    [InlineData("The shipping policy says that orders are packed within a day and sent with the courier. You can track the parcel on the website and change the address before it leaves the warehouse.", TextLanguage.English)]
    [InlineData("Siparişler bir gün içinde paketlenir ve kargoya verilir. Kargo takip numarası ile gönderiyi web sitesinden izleyebilir, depodan çıkmadan önce adresi değiştirebilirsiniz.", TextLanguage.Turkish)]
    [InlineData("12 34 56", TextLanguage.Simple)]
    public void The_language_decides_how_words_are_matched(string text, string expected)
    {
        Assert.Equal(expected, TextLanguage.Detect(text));
    }
}
