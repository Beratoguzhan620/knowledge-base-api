namespace KnowledgeBase.Core.Documents;

/// <summary>The uploaded file itself, kept apart from <see cref="Document"/> so lists never load it.</summary>
public sealed class DocumentFile
{
    private DocumentFile()
    {
    }

    public Guid DocumentId { get; private set; }

    public byte[] Content { get; private set; } = [];

    public static DocumentFile For(Document document, byte[] content) => new() { DocumentId = document.Id, Content = content };
}
