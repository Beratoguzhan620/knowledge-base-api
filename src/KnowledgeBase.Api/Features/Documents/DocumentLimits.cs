namespace KnowledgeBase.Api.Features.Documents;

/// <summary>How much a workspace may hold. Configured under <c>Limits</c>.</summary>
public sealed class DocumentLimits
{
    public const string Section = "Limits";

    public int MaxFileMegabytes { get; set; } = 10;

    public int WorkspaceStorageMegabytes { get; set; } = 100;

    /// <summary>The longest passage, in characters, a document is cut into.</summary>
    public int ChunkCharacters { get; set; } = 1200;

    public long MaxFileBytes => MaxFileMegabytes * 1024L * 1024L;

    public long WorkspaceStorageBytes => WorkspaceStorageMegabytes * 1024L * 1024L;
}
