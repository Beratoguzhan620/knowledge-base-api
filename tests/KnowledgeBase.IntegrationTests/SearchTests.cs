using System.Net;
using KnowledgeBase.IntegrationTests.Support;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SearchTests(ApiFactory factory)
{
    private static async Task<SearchDto> SearchAsync(Person person, string query, string mode = "Hybrid", Guid? workspaceId = null)
    {
        var response = await person.Http.GetAsync($"/api/workspaces/{workspaceId ?? person.WorkspaceId}/search?q={Uri.EscapeDataString(query)}&mode={mode}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<SearchDto>();
    }

    [Fact]
    public async Task A_question_finds_the_passage_that_answers_it_with_its_place_in_the_document()
    {
        using var person = await factory.RegisterAsync();
        var documents = await Corpus.AddAllAsync(person);
        await factory.IndexAsync(person.WorkspaceId);

        var result = await SearchAsync(person, "How many days until my refund is paid back to the card?");

        var top = result.Hits[0];
        Assert.Equal((documents["returns"], "Refunds", "Returns policy"), (top.DocumentId, top.Heading, top.DocumentTitle));
        Assert.Contains("3 working days", top.Text, StringComparison.Ordinal);
        Assert.NotNull(top.VectorRank);
        Assert.NotNull(top.KeywordRank);
        Assert.True(result.Hits.Zip(result.Hits.Skip(1)).All(p => p.First.Score >= p.Second.Score));
    }

    [Fact]
    public async Task Exact_words_like_product_codes_are_found_by_keywords_and_hybrid_keeps_them()
    {
        using var person = await factory.RegisterAsync();
        var documents = await Corpus.AddAllAsync(person);
        await factory.IndexAsync(person.WorkspaceId);

        var keyword = await SearchAsync(person, "KHV-002", "Keyword");
        var hybrid = await SearchAsync(person, "KHV-002");

        Assert.All(keyword.Hits, h => Assert.Null(h.VectorRank));
        Assert.Equal(("Product codes", documents["brewing"]), (keyword.Hits[0].Heading, keyword.Hits[0].DocumentId));
        Assert.Equal(keyword.Hits[0].ChunkId, hybrid.Hits[0].ChunkId);
    }

    [Fact]
    public async Task Vector_search_alone_ranks_every_ready_passage_by_meaning()
    {
        using var person = await factory.RegisterAsync();
        await Corpus.AddAllAsync(person);
        await factory.IndexAsync(person.WorkspaceId);

        var result = await SearchAsync(person, "which carriers deliver parcels", "Vector");

        Assert.All(result.Hits, h => Assert.Null(h.KeywordRank));
        Assert.Equal("Carriers", result.Hits[0].Heading);
        Assert.Equal(Enumerable.Range(1, result.Hits.Count), result.Hits.Select(h => h.VectorRank!.Value));
        Assert.True(result.Hits[0].Similarity > result.Hits[^1].Similarity);
    }

    [Fact]
    public async Task Documents_that_are_not_indexed_yet_are_not_searched()
    {
        using var person = await factory.RegisterAsync();
        await person.AddDocumentAsync("returns.md", Corpus.Returns);

        Assert.Empty((await SearchAsync(person, "refund")).Hits);

        await factory.IndexAsync(person.WorkspaceId);
        Assert.NotEmpty((await SearchAsync(person, "refund")).Hits);
    }

    [Fact]
    public async Task Search_never_crosses_into_another_workspace()
    {
        using var ayse = await factory.RegisterAsync();
        using var mehmet = await factory.RegisterAsync();
        var hers = await Corpus.AddAllAsync(ayse);
        await mehmet.AddDocumentAsync("returns-copy.md", Corpus.Returns + "\nA copy kept by another team.");
        await factory.IndexAsync(ayse.WorkspaceId);
        await factory.IndexAsync(mehmet.WorkspaceId);

        foreach (var mode in new[] { "Hybrid", "Vector", "Keyword" })
        {
            var result = await SearchAsync(ayse, "refund card days", mode);
            Assert.NotEmpty(result.Hits);
            Assert.All(result.Hits, h => Assert.Contains(h.DocumentId, hers.Values));
        }

        var outsider = await mehmet.Http.GetAsync($"/api/workspaces/{ayse.WorkspaceId}/search?q=refund");
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
    }

    [Fact]
    public async Task Turkish_documents_are_matched_with_turkish_stemming()
    {
        using var person = await factory.RegisterAsync();
        await person.AddDocumentAsync("iade.md", """
            # İade politikası

            Ürünler teslimattan sonraki 14 gün içinde iade edilebilir. İadeler kargo ile depomuza gönderilmelidir.

            ## Para iadesi

            Para iadesi, ürün depomuza ulaştıktan sonra 3 iş günü içinde kartınıza yapılır.
            """);
        await factory.IndexAsync(person.WorkspaceId);

        // "iadeleri" is not in the text; its stem is, in "iadesi" and "iade".
        var result = await SearchAsync(person, "para iadeleri", "Keyword");

        Assert.Equal("Para iadesi", result.Hits[0].Heading);
        Assert.Equal(2, result.Hits.Count);
        Assert.Equal("turkish", await factory.ScalarAsync<string>("SELECT language FROM documents WHERE workspace_id = @p0", person.WorkspaceId));
    }

    [Fact]
    public async Task Questions_must_have_text_and_a_sensible_limit()
    {
        using var person = await factory.RegisterAsync();
        var root = $"/api/workspaces/{person.WorkspaceId}/search";

        Assert.Equal(HttpStatusCode.BadRequest, (await person.Http.GetAsync($"{root}?q=%20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await person.Http.GetAsync($"{root}?q=x&limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await person.Http.GetAsync($"{root}?q={new string('x', 501)}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await person.Http.GetAsync($"{root}?q=anything")).StatusCode);
    }
}
