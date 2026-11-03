using System.Text;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Ingestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace KnowledgeBase.Infrastructure.Ingestion;

/// <summary>Gets the text out of an uploaded file. Anything it cannot read is refused with a reason a person can act on.</summary>
public static class DocumentReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static ExtractedText Read(DocumentFormat format, byte[] content) => format switch
    {
        DocumentFormat.Markdown => MarkdownReader.Read(DecodeText(content)),
        DocumentFormat.PlainText => PlainTextReader.Read(DecodeText(content)),
        DocumentFormat.Pdf => ReadPdf(content),
        _ => throw new DomainException(ErrorCodes.UnsupportedFile, "This kind of file cannot be read."),
    };

    /// <summary>UTF-8 (with or without a byte order mark) or UTF-16 with one; anything else is not taken as text.</summary>
    private static string DecodeText(byte[] content)
    {
        if (content.AsSpan().IndexOf((byte)0) >= 0 && !(content.Length >= 2 && (content[0], content[1]) is (0xFF, 0xFE) or (0xFE, 0xFF)))
        {
            throw new DomainException(ErrorCodes.UnsupportedFile, "This does not look like a text file.");
        }

        try
        {
            if (content.Length >= 2 && (content[0], content[1]) is (0xFF, 0xFE))
            {
                return Encoding.Unicode.GetString(content, 2, content.Length - 2);
            }

            if (content.Length >= 2 && (content[0], content[1]) is (0xFE, 0xFF))
            {
                return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
            }

            var offset = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
            return StrictUtf8.GetString(content, offset, content.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            throw new DomainException(ErrorCodes.UnsupportedFile, "The file is not UTF-8 text. Save it as UTF-8 and upload it again.");
        }
    }

    private static ExtractedText ReadPdf(byte[] content)
    {
        // The header may follow a little junk, which readers accept; it must be in the first kilobyte.
        if (content.AsSpan(0, Math.Min(content.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            throw new DomainException(ErrorCodes.UnsupportedFile, "This is not a PDF file.");
        }

        try
        {
            using var pdf = PdfDocument.Open(content);
            var sections = new List<Section>();
            foreach (var page in pdf.GetPages())
            {
                var text = ContentOrderTextExtractor.GetText(page).Replace("\r\n", "\n").Trim();
                if (text.Length > 0)
                {
                    sections.Add(new Section(null, page.Number, text));
                }
            }

            var title = pdf.Information.Title;
            return new ExtractedText(string.IsNullOrWhiteSpace(title) ? null : title.Trim(), sections);
        }
        catch (Exception ex) when (ex is not DomainException and not OutOfMemoryException)
        {
            throw new DomainException(ErrorCodes.UnsupportedFile, "The PDF could not be read. It may be damaged or protected with a password.");
        }
    }
}
