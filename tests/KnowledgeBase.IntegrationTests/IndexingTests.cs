using System.Net;
using KnowledgeBase.Api.Features.Indexing;
using KnowledgeBase.Core;
using KnowledgeBase.Infrastructure.Embeddings;
using KnowledgeBase.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class IndexingTests(ApiFactory factory)
{
    [Fact]
    public async Task Indexing_embeds_every_passage_and_makes_the_document_ready()
    {
        using var person = await factory.RegisterAsync();
        var document = await person.AddDocumentAsync("returns.md", Corpus.Returns);

        Assert.Equal(1, await factory.IndexAsync(person.WorkspaceId));

        var indexed = await (await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/documents/{document.Id}")).ReadAsync<DocumentDto>();
        Assert.Equal(("Ready", LocalEmbeddingGenerator.ModelName, (string?)null), (indexed.Status, indexed.EmbeddingModel, indexed.Error));
        Assert.NotNull(indexed.IndexedAt);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM chunks WHERE document_id = @p0 AND embedding IS NULL", document.Id));
        Assert.Equal(768, await factory.ScalarAsync<int>("SELECT vector_dims(embedding) FROM chunks WHERE document_id = @p0 LIMIT 1", document.Id));

        var usage = await (await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/usage")).ReadAsync<UsageDto>();
        Assert.Equal((1, 0, 3), (usage.Ready, usage.Queued, usage.Chunks));
    }

    [Fact]
    public async Task Workers_running_at_the_same_time_never_take_the_same_document()
    {
        using var person = await factory.RegisterAsync();
        var ids = (await Corpus.AddAllAsync(person)).Values.ToList();
        var indexer = factory.Services.GetRequiredService<DocumentIndexer>();

        var claimed = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => indexer.IndexNextAsync(person.WorkspaceId, CancellationToken.None)));

        Assert.Equal(3, claimed.Count(c => c));
        foreach (var id in ids)
        {
            Assert.Equal(1, await factory.ScalarAsync<int>("SELECT attempts FROM documents WHERE id = @p0", id));
            Assert.Equal("Ready", await factory.ScalarAsync<string>("SELECT status FROM documents WHERE id = @p0", id));
        }
    }

    [Fact]
    public async Task A_failing_embedding_service_is_retried_later_then_reported_and_the_document_can_be_queued_again()
    {
        using var person = await factory.RegisterAsync();
        var document = await person.AddDocumentAsync("returns.md", Corpus.Returns);
        await using var broken = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmbeddingGenerator>();
            s.AddSingleton<IEmbeddingGenerator>(new UnreachableGenerator());
        }));
        var brokenIndexer = broken.Services.GetRequiredService<DocumentIndexer>();
        var path = $"/api/workspaces/{person.WorkspaceId}/documents/{document.Id}";

        Assert.True(await brokenIndexer.IndexNextAsync(person.WorkspaceId, CancellationToken.None));
        var first = await (await person.Http.GetAsync(path)).ReadAsync<DocumentDto>();
        Assert.Equal("Queued", first.Status);
        Assert.Contains("not answering", first.Error, StringComparison.Ordinal);
        Assert.False(await brokenIndexer.IndexNextAsync(person.WorkspaceId, CancellationToken.None)); // the retry is not due yet

        await factory.ScalarAsync<int>("UPDATE documents SET next_attempt_at = now() - interval '1 second' WHERE id = @p0 RETURNING 1", document.Id);
        Assert.True(await brokenIndexer.IndexNextAsync(person.WorkspaceId, CancellationToken.None));
        var failed = await (await person.Http.GetAsync(path)).ReadAsync<DocumentDto>();
        Assert.Equal("Failed", failed.Status);
        Assert.False(await brokenIndexer.IndexNextAsync(person.WorkspaceId, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Accepted, (await person.Http.PostAsync($"{path}/reindex", null)).StatusCode);
        await factory.IndexAsync(person.WorkspaceId);
        Assert.Equal("Ready", (await (await person.Http.GetAsync(path)).ReadAsync<DocumentDto>()).Status);
    }

    [Fact]
    public async Task A_document_left_behind_by_a_worker_that_died_is_taken_over_when_its_lease_runs_out()
    {
        using var person = await factory.RegisterAsync();
        var document = await person.AddDocumentAsync("returns.md", Corpus.Returns);
        await factory.ScalarAsync<int>("UPDATE documents SET status = 'Indexing', attempts = 1, next_attempt_at = now() + interval '5 minutes' WHERE id = @p0 RETURNING 1", document.Id);

        Assert.Equal(0, await factory.IndexAsync(person.WorkspaceId));

        await factory.ScalarAsync<int>("UPDATE documents SET next_attempt_at = now() - interval '1 second' WHERE id = @p0 RETURNING 1", document.Id);
        Assert.Equal(1, await factory.IndexAsync(person.WorkspaceId));
        Assert.Equal("Ready", await factory.ScalarAsync<string>("SELECT status FROM documents WHERE id = @p0", document.Id));
    }

    [Fact]
    public async Task Owners_reindex_the_documents_of_an_old_model_when_the_model_changes()
    {
        using var owner = await factory.RegisterAsync();
        using var reader = await factory.RegisterAsync();
        await owner.AddAsync(reader, "Reader");
        await Corpus.AddAllAsync(owner);
        await factory.IndexAsync(owner.WorkspaceId);
        await using var upgraded = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmbeddingGenerator>();
            s.AddSingleton<IEmbeddingGenerator>(new RenamedGenerator("local-hashing-v2"));
        }));
        using var ownerOnNewModel = upgraded.CreateClient();
        ownerOnNewModel.DefaultRequestHeaders.Authorization = owner.Http.DefaultRequestHeaders.Authorization;
        using var readerOnNewModel = upgraded.CreateClient();
        readerOnNewModel.DefaultRequestHeaders.Authorization = reader.Http.DefaultRequestHeaders.Authorization;

        Assert.Equal(HttpStatusCode.Forbidden, (await readerOnNewModel.PostAsync($"/api/workspaces/{owner.WorkspaceId}/reindex", null)).StatusCode);
        var queued = await (await ownerOnNewModel.PostAsync($"/api/workspaces/{owner.WorkspaceId}/reindex", null)).ReadAsync<ReindexDto>();
        Assert.Equal(3, queued.Queued);

        var indexer = upgraded.Services.GetRequiredService<DocumentIndexer>();
        while (await indexer.IndexNextAsync(owner.WorkspaceId, CancellationToken.None))
        {
        }

        Assert.Equal(3L, await factory.ScalarAsync<long>("SELECT count(*) FROM documents WHERE workspace_id = @p0 AND embedding_model = 'local-hashing-v2'", owner.WorkspaceId));
        var again = await (await ownerOnNewModel.PostAsync($"/api/workspaces/{owner.WorkspaceId}/reindex", null)).ReadAsync<ReindexDto>();
        Assert.Equal(0, again.Queued);
    }

    private sealed record ReindexDto(int Queued);

    private sealed class UnreachableGenerator : IEmbeddingGenerator
    {
        public string Model => "unreachable";

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct) =>
            throw new DomainException(ErrorCodes.ProviderUnavailable, "The embedding service at http://ollama:11434/v1/ is not answering.");
    }

    private sealed class RenamedGenerator(string model) : IEmbeddingGenerator
    {
        private readonly LocalEmbeddingGenerator _inner = new();

        public string Model => model;

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct) =>
            _inner.EmbedAsync(texts, purpose, ct);
    }
}
