namespace KnowledgeBase.Core.Documents;

public enum DocumentFormat
{
    Markdown = 0,
    PlainText = 1,
    Pdf = 2,
}

public static class DocumentFormats
{
    /// <summary>The format a file name says it has, or <c>null</c> when it is not one the knowledge base reads.</summary>
    public static DocumentFormat? FromFileName(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".md" or ".markdown" => DocumentFormat.Markdown,
        ".txt" or ".text" => DocumentFormat.PlainText,
        ".pdf" => DocumentFormat.Pdf,
        _ => null,
    };

    public static string ContentType(DocumentFormat format) => format switch
    {
        DocumentFormat.Markdown => "text/markdown; charset=utf-8",
        DocumentFormat.PlainText => "text/plain; charset=utf-8",
        _ => "application/pdf",
    };
}
