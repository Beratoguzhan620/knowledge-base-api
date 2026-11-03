using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using KnowledgeBase.Core;
using KnowledgeBase.Infrastructure.Answers;
using KnowledgeBase.Infrastructure.Embeddings;
using KnowledgeBase.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class QuestionTests(ApiFactory factory)
{
    private async Task<(Person Person, Dictionary<string, Guid> Documents)> ReadyWorkspaceAsync()
    {
        var person = await factory.RegisterAsync();
        var documents = await Corpus.AddAllAsync(person);
        await factory.IndexAsync(person.WorkspaceId);
        return (person, documents);
    }

    [Fact]
    public async Task A_question_is_answered_from_the_documents_with_citations_and_stored()
    {
        var (person, documents) = await ReadyWorkspaceAsync();
        using var _ = person;
        var conversation = await person.StartConversationAsync();

        var result = await person.AskAsync(conversation.Id, "How many days until a refund reaches the card?");

        Assert.Equal(("Answered", ExtractiveAnswerGenerator.ModelName), (result.Answer.Outcome, result.Answer.Model));
        Assert.Contains("3 working days", result.Answer.Content, StringComparison.Ordinal);
        var citation = Assert.Single(result.Answer.Citations);
        Assert.Equal((documents["returns"], "Refunds", "Returns policy"), (citation.DocumentId, citation.Heading, citation.DocumentTitle));
        Assert.Contains($"[{citation.Number}]", result.Answer.Content, StringComparison.Ordinal);
        Assert.Contains(result.Sources, s => s.ChunkId == citation.ChunkId);

        var stored = await (await person.Http.GetAsync($"/api/workspaces/{person.WorkspaceId}/conversations/{conversation.Id}")).ReadAsync<ConversationDetailDto>();
        Assert.Equal("How many days until a refund reaches the card?", stored.Title);
        Assert.Equal([("User", result.Question.Content), ("Assistant", result.Answer.Content)], stored.Messages.Select(m => (m.Role, m.Content)));
    }

    [Fact]
    public async Task Nothing_relevant_means_no_answer_and_no_model_call()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        var model = new RecordingGenerator();
        await using var app = WithGenerator(model);
        using var http = ClientFor(app, person);
        var conversation = await person.StartConversationAsync();

        var english = await AskAsync(http, person, conversation.Id, "Who painted the Mona Lisa?");
        var turkish = await AskAsync(http, person, conversation.Id, "Mona Lisa tablosunu kim çizdi?");

        Assert.Equal(("NotFound", 0), (english.Answer.Outcome, english.Sources.Count));
        Assert.StartsWith("I could not find", english.Answer.Content, StringComparison.Ordinal);
        Assert.StartsWith("Bu çalışma alanındaki belgelerde", turkish.Answer.Content, StringComparison.Ordinal);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task The_model_gets_numbered_sources_and_the_earlier_turns_of_the_conversation()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        var model = new RecordingGenerator();
        await using var app = WithGenerator(model);
        using var http = ClientFor(app, person);
        var conversation = await person.StartConversationAsync();

        await AskAsync(http, person, conversation.Id, "How long do refunds take?");
        var followUp = await AskAsync(http, person, conversation.Id, "and exchanges?");

        Assert.Equal(2, model.Requests.Count);
        var second = model.Requests[1];
        Assert.Equal(["How long do refunds take?", "Recorded answer [1]."], second.History.Select(h => h.Content));
        Assert.Equal(Enumerable.Range(1, second.Sources.Count), second.Sources.Select(s => s.Number));
        Assert.Contains(second.Sources, s => s.Heading == "Exchanges");
        Assert.Equal(1, Assert.Single(followUp.Answer.Citations).Number);
    }

    [Fact]
    public async Task An_unrelated_question_after_an_answered_one_is_not_answered_from_the_earlier_passages()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        var conversation = await person.StartConversationAsync();

        await person.AskAsync(conversation.Id, "How long do refunds take?");
        var unrelated = await person.AskAsync(conversation.Id, "Who painted the Mona Lisa?");

        Assert.Equal("NotFound", unrelated.Answer.Outcome);
    }

    [Fact]
    public async Task Answers_stream_as_server_sent_events_sources_first_then_text_then_the_stored_answer()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        var conversation = await person.StartConversationAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{person.WorkspaceId}/conversations/{conversation.Id}/messages")
        {
            Content = JsonContent.Create(new { question = "Which carrier delivers the parcels?" }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await person.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var events = ParseEvents(await response.Content.ReadAsStringAsync());

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("sources", events[0].Name);
        Assert.Equal("done", events[^1].Name);
        Assert.All(events[1..^1], e => Assert.Equal("delta", e.Name));
        var streamed = string.Concat(events[1..^1].Select(e => e.Data.GetProperty("text").GetString()));
        var stored = events[^1].Data.GetProperty("answer");
        Assert.Equal(streamed.Trim(), stored.GetProperty("content").GetString());
        Assert.Contains("Yurtiçi Kargo", streamed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_fails_leaves_a_stored_failed_answer_instead_of_an_error()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        await using var app = WithGenerator(new FailingGenerator());
        using var http = ClientFor(app, person);
        var conversation = await person.StartConversationAsync();

        var result = await AskAsync(http, person, conversation.Id, "How long do refunds take?");

        Assert.Equal("Failed", result.Answer.Outcome);
        Assert.StartsWith("The documents", result.Answer.Content, StringComparison.Ordinal); // what came before the failure is kept
        Assert.Contains("could not be reached", result.Answer.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conversations_are_private_even_from_the_workspace_s_owner()
    {
        var (owner, _) = await ReadyWorkspaceAsync();
        using var __ = owner;
        using var reader = await factory.RegisterAsync();
        using var outsider = await factory.RegisterAsync();
        await owner.AddAsync(reader, "Reader");
        var conversation = await reader.StartConversationAsync(owner.WorkspaceId);
        await reader.AskAsync(conversation.Id, "How long do refunds take?", owner.WorkspaceId);
        var path = $"/api/workspaces/{owner.WorkspaceId}/conversations/{conversation.Id}";

        Assert.Equal(HttpStatusCode.OK, (await reader.Http.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync(path)).StatusCode);
        Assert.Empty(await (await owner.Http.GetAsync($"/api/workspaces/{owner.WorkspaceId}/conversations")).ReadAsync<List<ConversationDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Http.GetAsync(path)).StatusCode);

        (await owner.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}/members/{reader.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await reader.Http.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Citations_stay_readable_after_the_document_is_deleted_and_answers_can_be_rated()
    {
        var (person, documents) = await ReadyWorkspaceAsync();
        using var __ = person;
        var conversation = await person.StartConversationAsync();
        var result = await person.AskAsync(conversation.Id, "How long do refunds take?");
        var root = $"/api/workspaces/{person.WorkspaceId}/conversations/{conversation.Id}";

        (await person.Http.DeleteAsync($"/api/workspaces/{person.WorkspaceId}/documents/{documents["returns"]}")).EnsureSuccessStatusCode();
        var rated = await (await person.Http.PutAsJsonAsync($"{root}/messages/{result.Answer.Id}/feedback", new { helpful = true })).ReadAsync<MessageDto>();
        var ratingAQuestion = await person.Http.PutAsJsonAsync($"{root}/messages/{result.Question.Id}/feedback", new { helpful = false });

        var stored = await (await person.Http.GetAsync(root)).ReadAsync<ConversationDetailDto>();
        Assert.Contains("Refunds", stored.Messages[1].Citations[0].Quote, StringComparison.Ordinal);
        Assert.True(rated.Helpful);
        Assert.Equal(HttpStatusCode.Conflict, ratingAQuestion.StatusCode);
    }

    [Fact]
    public async Task Questions_are_limited_per_person_per_minute_and_must_have_text()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        await using var app = factory.WithWebHostBuilder(b => b.UseSetting("Answers:QuestionsPerMinute", "2"));
        using var http = ClientFor(app, person);
        var conversation = await person.StartConversationAsync();
        var path = $"/api/workspaces/{person.WorkspaceId}/conversations/{conversation.Id}/messages";

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path, new { question = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(path, new { question = "refunds?" })).StatusCode);
        var limited = await http.PostAsJsonAsync(path, new { question = "exchanges?" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await limited.ProblemCodeAsync());
    }

    [Fact]
    public async Task Conversations_are_listed_latest_first_renamed_and_deleted()
    {
        var (person, _) = await ReadyWorkspaceAsync();
        using var __ = person;
        var first = await person.StartConversationAsync();
        var second = await person.StartConversationAsync();
        await person.AskAsync(first.Id, "How long do refunds take?");
        var root = $"/api/workspaces/{person.WorkspaceId}/conversations";

        var list = await (await person.Http.GetAsync(root)).ReadAsync<List<ConversationDto>>();
        Assert.Equal([first.Id, second.Id], list.Select(c => c.Id));

        var renamed = await (await person.Http.PatchAsJsonAsync($"{root}/{second.Id}", new { title = "Kargo soruları" })).ReadAsync<ConversationDto>();
        Assert.Equal("Kargo soruları", renamed.Title);
        Assert.Equal(HttpStatusCode.NoContent, (await person.Http.DeleteAsync($"{root}/{first.Id}")).StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM messages WHERE conversation_id = @p0", first.Id));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithGenerator(IAnswerGenerator generator) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IAnswerGenerator>();
            s.AddSingleton(generator);
        }));

    private static HttpClient ClientFor(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, Person person)
    {
        var http = app.CreateClient();
        http.DefaultRequestHeaders.Authorization = person.Http.DefaultRequestHeaders.Authorization;
        return http;
    }

    private static async Task<AnswerDto> AskAsync(HttpClient http, Person person, Guid conversationId, string question)
    {
        var response = await http.PostAsJsonAsync($"/api/workspaces/{person.WorkspaceId}/conversations/{conversationId}/messages", new { question });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AnswerDto>();
    }

    private static (string Name, JsonElement Data)[] ParseEvents(string body) =>
        body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Split('\n'))
            .Select(lines => (
                Name: lines.Single(l => l.StartsWith("event:", StringComparison.Ordinal))[6..].Trim(),
                Data: JsonDocument.Parse(string.Join('\n', lines.Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()))).RootElement))
            .ToArray();

    private sealed class RecordingGenerator : IAnswerGenerator
    {
        public List<AnswerRequest> Requests { get; } = [];

        public string Model => "recording";

        public async IAsyncEnumerable<string> StreamAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request);
            await Task.Yield();
            yield return "Recorded answer [1].";
        }
    }

    private sealed class FailingGenerator : IAnswerGenerator
    {
        public string Model => "failing";

        public async IAsyncEnumerable<string> StreamAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return "The documents say something, ";
            await Task.Yield();
            throw new DomainException(ErrorCodes.ProviderUnavailable, "The chat model is not answering.");
        }
    }
}
