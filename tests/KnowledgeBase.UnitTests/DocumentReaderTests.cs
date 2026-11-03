using System.Text;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Infrastructure.Ingestion;

namespace KnowledgeBase.UnitTests;

public sealed class DocumentReaderTests
{
    [Fact]
    public void Text_is_read_as_utf8_with_or_without_a_byte_order_mark()
    {
        var plain = DocumentReader.Read(DocumentFormat.PlainText, Encoding.UTF8.GetBytes("İade süresi 14 gündür."));
        var withBom = DocumentReader.Read(DocumentFormat.PlainText, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("İade süresi 14 gündür.")]);
        var utf16 = DocumentReader.Read(DocumentFormat.PlainText, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("İade süresi 14 gündür.")]);

        Assert.Equal("İade süresi 14 gündür.", plain.Sections.Single().Text);
        Assert.Equal(plain.Sections.Single().Text, withBom.Sections.Single().Text);
        Assert.Equal(plain.Sections.Single().Text, utf16.Sections.Single().Text);
    }

    [Fact]
    public void Text_in_another_encoding_or_binary_data_is_refused()
    {
        var latin5 = new byte[] { 0x49, 0x61, 0x64, 0x65, 0x20, 0xFD, 0xFE }; // Windows-1254 bytes, not valid UTF-8
        var binary = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 };

        Assert.Equal(ErrorCodes.UnsupportedFile, Assert.Throws<DomainException>(() => DocumentReader.Read(DocumentFormat.PlainText, latin5)).Code);
        Assert.Equal(ErrorCodes.UnsupportedFile, Assert.Throws<DomainException>(() => DocumentReader.Read(DocumentFormat.Markdown, binary)).Code);
    }

    [Fact]
    public void A_pdf_is_read_page_by_page_with_its_page_numbers()
    {
        var pdf = Pdfs.Make(["Returns are accepted within 14 days."], [], ["Refunds reach the card in 3 days."]);

        var result = DocumentReader.Read(DocumentFormat.Pdf, pdf);

        Assert.Equal([1, 3], result.Sections.Select(s => s.Page!.Value));
        Assert.Contains("14 days", result.Sections[0].Text, StringComparison.Ordinal);
        Assert.Contains("3 days", result.Sections[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Something_that_is_not_a_pdf_is_refused_with_a_reason()
    {
        var notPdf = Encoding.UTF8.GetBytes("just text pretending");
        var broken = "%PDF-1.7\n1 0 obj << /Type /Catalog >>\n"u8.ToArray();

        Assert.Equal("This is not a PDF file.", Assert.Throws<DomainException>(() => DocumentReader.Read(DocumentFormat.Pdf, notPdf)).Message);

        // A damaged PDF either cannot be opened, or yields no text; both end as an unreadable file at upload.
        try
        {
            Assert.Empty(DocumentReader.Read(DocumentFormat.Pdf, broken).Sections);
        }
        catch (DomainException error)
        {
            Assert.Equal(ErrorCodes.UnsupportedFile, error.Code);
        }
    }
}
