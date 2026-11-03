using KnowledgeBase.Core;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Ingestion;

namespace KnowledgeBase.UnitTests;

public sealed class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private static Document NewDocument(string title = "", string fileName = "kargo-politikasi.md", int chunks = 3) =>
        Document.Create(Guid.NewGuid(), title, fileName, DocumentFormat.Markdown, 1200, [1, 2, 3], TextLanguage.Turkish, chunks, 1100, Guid.NewGuid(), Now);

    [Fact]
    public void A_new_document_waits_in_the_queue_and_is_named_after_its_file_without_a_title()
    {
        var document = NewDocument();

        Assert.Equal("kargo-politikasi", document.Title);
        Assert.Equal(DocumentStatus.Queued, document.Status);
        Assert.Equal(Now, document.NextAttemptAt);
        Assert.Equal(0, document.Attempts);
    }

    [Fact]
    public void A_file_without_text_is_refused()
    {
        var error = Assert.Throws<DomainException>(() => NewDocument(chunks: 0));
        Assert.Equal(ErrorCodes.UnsupportedFile, error.Code);
    }

    [Fact]
    public void Indexing_counts_attempts_and_ends_ready_with_the_model()
    {
        var document = NewDocument();
        document.StartIndexing(Now, Now.AddMinutes(10));
        Assert.Equal((DocumentStatus.Indexing, 1, Now.AddMinutes(10)), (document.Status, document.Attempts, document.NextAttemptAt));

        document.MarkReady("nomic-embed-text", Now.AddSeconds(5));

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal("nomic-embed-text", document.EmbeddingModel);
        Assert.Equal(Now.AddSeconds(5), document.IndexedAt);
    }

    [Fact]
    public void A_failed_attempt_goes_back_in_the_queue_for_later_until_it_gives_up()
    {
        var document = NewDocument();
        document.StartIndexing(Now, Now.AddMinutes(10));
        document.MarkFailed("The embedding service did not answer.", Now.AddSeconds(30));
        Assert.Equal((DocumentStatus.Queued, Now.AddSeconds(30)), (document.Status, document.NextAttemptAt));

        document.StartIndexing(Now.AddSeconds(30), Now.AddMinutes(10));
        document.MarkFailed("The embedding service did not answer.", retryAt: null);
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal(2, document.Attempts);

        document.Requeue(Now.AddMinutes(1));
        Assert.Equal((DocumentStatus.Queued, 0, (string?)null), (document.Status, document.Attempts, document.Error));
    }

    [Fact]
    public void A_document_being_indexed_is_left_alone_until_its_worker_s_lease_runs_out()
    {
        var document = NewDocument();
        document.StartIndexing(Now, Now.AddMinutes(10));

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => document.StartIndexing(Now.AddMinutes(5), Now.AddMinutes(15))).Code);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => document.Requeue(Now.AddMinutes(5))).Code);

        // The first worker died; after its lease another one takes over.
        document.StartIndexing(Now.AddMinutes(11), Now.AddMinutes(21));
        Assert.Equal((DocumentStatus.Indexing, 2), (document.Status, document.Attempts));
    }

    [Fact]
    public void Chunks_carry_the_workspace_and_a_heading_that_fits()
    {
        var document = NewDocument();
        var chunk = Chunk.Create(document, new ChunkDraft(2, "text", new string('h', 800), 4));

        Assert.Equal((document.WorkspaceId, document.Id, 2, (int?)4), (chunk.WorkspaceId, chunk.DocumentId, chunk.Ordinal, chunk.Page));
        Assert.Equal(Chunk.HeadingMaxLength, chunk.Heading!.Length);
        Assert.Equal(TextLanguage.Turkish, chunk.Language);
    }

    [Theory]
    [InlineData("notes.md", DocumentFormat.Markdown)]
    [InlineData("NOTES.MARKDOWN", DocumentFormat.Markdown)]
    [InlineData("readme.txt", DocumentFormat.PlainText)]
    [InlineData("handbook.pdf", DocumentFormat.Pdf)]
    [InlineData("sheet.xlsx", null)]
    [InlineData("noextension", null)]
    public void The_file_name_decides_the_format(string fileName, DocumentFormat? expected)
    {
        Assert.Equal(expected, DocumentFormats.FromFileName(fileName));
    }
}
