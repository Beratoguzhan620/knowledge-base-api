using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace KnowledgeBase.IntegrationTests.Support;

/// <summary>Small real PDFs made on the spot, one line list per page.</summary>
internal static class Pdfs
{
    public static byte[] Make(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780.0;
            foreach (var line in lines)
            {
                page.AddText(line, 11, new PdfPoint(50, y), font);
                y -= 16;
            }
        }

        return builder.Build();
    }
}
