using System.Net;
using System.Net.Http.Json;
using System.Text;
using KnowledgeBase.IntegrationTests.Support;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DocumentTests(ApiFactory factory)
{
    private const string Handbook = """
        # Returns handbook

        Customers can send back any product within 14 days of delivery.

        ## Refunds

        Refunds reach the original card within 3 working days of the parcel arriving at the warehouse.

        ## Exchanges

        An exchange for another size or colour is free once per order.
        """;

    [Fact]
    public async Task A_markdown_file_is_read_split_by_heading_and_queued_for_indexing()
    {
        using var person = await factory.RegisterAsync("Elif Şahin");

        var response = await person.UploadAsync("returns.md", Handbook);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var document = await response.ReadAsync<DocumentDto>();
        Assert.Equal(("Returns handbook", "returns.md", "Markdown", "Queued", "english", "Elif Şahin"),
            (document.Title, document.FileName, document.Format, document.Status, document.Language, document.UploadedBy));
        Assert.Equal(3, document.ChunkCount);

        var chunks = await (await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/documents/{document.Id}/chunks")).ReadAsync<ChunkPageDto>();
        Assert.Equal(new string?[] { null, "Refunds", "Exchanges" }, chunks.Items.Select(c => c.Heading));
        Assert.Contains("3 working days", chunks.Items[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pdf_keeps_its_page_numbers_and_a_given_title_wins()
    {
        using var person = await factory.RegisterAsync();
        var pdf = Pdfs.Make(["Opening hours are 9 to 18 on weekdays."], ["Saturday opening hours are 10 to 14."]);

        var response = await person.UploadAsync("hours.pdf", pdf, title: "Opening hours");
        var document = await response.ReadAsync<DocumentDto>();
        var chunks = await (await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/documents/{document.Id}/chunks")).ReadAsync<ChunkPageDto>();

        Assert.Equal(("Opening hours", "Pdf"), (document.Title, document.Format));
        Assert.Equal([1, 2], chunks.Items.Select(c => c.Page!.Value));
    }

    [Fact]
    public async Task The_same_file_twice_in_a_workspace_is_refused_but_another_workspace_may_have_it()
    {
        using var ayse = await factory.RegisterAsync();
        using var mehmet = await factory.RegisterAsync();
        await ayse.AddDocumentAsync("returns.md", Handbook);

        var again = await ayse.UploadAsync("returns-copy.md", Handbook);
        var elsewhere = await mehmet.UploadAsync("returns.md", Handbook);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("duplicate_document", await again.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Accepted, elsewhere.StatusCode);
    }

    [Fact]
    public async Task Files_that_cannot_be_read_are_refused_with_a_reason()
    {
        using var person = await factory.RegisterAsync();

        var spreadsheet = await person.UploadAsync("prices.xlsx", [0x50, 0x4B, 0x03, 0x04]);
        var fakePdf = await person.UploadAsync("scan.pdf", Encoding.UTF8.GetBytes("this is not a pdf"));
        var notUtf8 = await person.UploadAsync("old.txt", [0x49, 0x61, 0x64, 0x65, 0xFD, 0xFE, 0x20, 0x41]);
        var empty = await person.UploadAsync("empty.md", "   \n\n ");
        var noFile = await person.Http.PostAsync($"/api/workspaces/{person.WorkspaceId}/documents", new MultipartFormDataContent { { new StringContent("x"), "title" } });

        Assert.All(new[] { spreadsheet, fakePdf, notUtf8, empty }, r => Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode));
        Assert.Equal("unsupported_file", await spreadsheet.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
    }

    [Fact]
    public async Task Files_over_the_size_limit_and_workspaces_over_their_storage_are_refused()
    {
        using var person = await factory.RegisterAsync();
        string Filler(char c) => "# Filler\n\n" + string.Join("\n\n", Enumerable.Range(0, 900).Select(i => $"{c} line {i} " + new string(c, 1000)));

        var tooLarge = await person.UploadAsync("big.md", Filler('a') + Filler('b'));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal("file_too_large", await tooLarge.ProblemCodeAsync());

        foreach (var c in "cde")
        {
            Assert.Equal(HttpStatusCode.Accepted, (await person.UploadAsync($"{c}.md", Filler(c))).StatusCode);
        }

        var full = await person.UploadAsync("f.md", Filler('f'));
        Assert.Equal(HttpStatusCode.InsufficientStorage, full.StatusCode);
        Assert.Equal("storage_limit_reached", await full.ProblemCodeAsync());

        var usage = await (await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/usage")).ReadAsync<UsageDto>();
        Assert.Equal(3, usage.Documents);
        Assert.Equal(ApiFactory.WorkspaceStorageMegabytes * 1024L * 1024L, usage.StorageLimitBytes);
        Assert.True(usage.StorageBytes <= usage.StorageLimitBytes);
    }

    [Fact]
    public async Task Documents_are_listed_newest_first_searched_by_name_renamed_and_deleted()
    {
        using var person = await factory.RegisterAsync();
        var shipping = await person.AddDocumentAsync("shipping.md", "# Shipping\n\nWe ship in two days.");
        var returns = await person.AddDocumentAsync("returns.md", Handbook);
        var root = $"/api/workspaces/{person.WorkspaceId}/documents";

        var all = await (await person.Http.GetAsync(root)).ReadAsync<DocumentPageDto>();
        var found = await (await person.Http.GetAsync($"{root}?q=SHIP")).ReadAsync<DocumentPageDto>();
        var queued = await (await person.Http.GetAsync($"{root}?status=Queued&pageSize=1")).ReadAsync<DocumentPageDto>();
        Assert.Equal([returns.Id, shipping.Id], all.Items.Select(d => d.Id));
        Assert.Equal([shipping.Id], found.Items.Select(d => d.Id));
        Assert.Equal((2, 1), (queued.Total, queued.Items.Count));

        var renamed = await (await person.Http.PatchAsJsonAsync($"{root}/{shipping.Id}", new { title = "Kargo süreleri" })).ReadAsync<DocumentDto>();
        Assert.Equal("Kargo süreleri", renamed.Title);

        Assert.Equal(HttpStatusCode.NoContent, (await person.Http.DeleteAsync($"{root}/{shipping.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await person.Http.GetAsync($"{root}/{shipping.Id}")).StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM chunks WHERE document_id = @p0", shipping.Id));
    }

    [Fact]
    public async Task The_original_file_downloads_as_an_attachment()
    {
        using var person = await factory.RegisterAsync();
        var document = await person.AddDocumentAsync("returns.md", Handbook);

        var response = await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/documents/{document.Id}/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("returns.md", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Handbook, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Another_workspace_s_documents_do_not_exist_for_outsiders()
    {
        using var ayse = await factory.RegisterAsync();
        using var mehmet = await factory.RegisterAsync();
        var document = await ayse.AddDocumentAsync("returns.md", Handbook);
        var theirs = $"/api/workspaces/{ayse.WorkspaceId}/documents/{document.Id}";
        var smuggled = $"/api/workspaces/{mehmet.WorkspaceId}/documents/{document.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await mehmet.Http.GetAsync(theirs)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mehmet.Http.GetAsync($"{theirs}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mehmet.Http.GetAsync(smuggled)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mehmet.Http.GetAsync($"{smuggled}/chunks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mehmet.Http.DeleteAsync(smuggled)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ayse.Http.GetAsync(theirs)).StatusCode);
    }

    [Fact]
    public async Task Reindexing_puts_a_document_back_in_the_queue()
    {
        using var person = await factory.RegisterAsync();
        var document = await person.AddDocumentAsync("returns.md", Handbook);

        var response = await person.Http.PostAsync($"/api/workspaces/{person.WorkspaceId}/documents/{document.Id}/reindex", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Queued", (await response.ReadAsync<DocumentDto>()).Status);
    }
}
