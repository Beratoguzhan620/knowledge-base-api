using System.Net.Http.Json;

namespace KnowledgeBase.IntegrationTests.Support;

public sealed record CitationDto(int Number, Guid ChunkId, Guid DocumentId, string DocumentTitle, string? Heading, int? Page, string Quote);

public sealed record MessageDto(Guid Id, string Role, string Content, string? Outcome, string? Model, List<CitationDto> Citations, bool? Helpful, int? DurationMs, DateTimeOffset CreatedAt);

public sealed record SourceDto(int Number, Guid ChunkId, Guid DocumentId, string DocumentTitle, string? Heading, int? Page, string Quote);

public sealed record AnswerDto(List<SourceDto> Sources, MessageDto Question, MessageDto Answer);

public sealed record ConversationDto(Guid Id, string? Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ConversationDetailDto(Guid Id, string? Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, List<MessageDto> Messages);

public static class Questions
{
    public static async Task<ConversationDto> StartConversationAsync(this Person person, Guid? workspaceId = null)
    {
        var response = await person.Http.PostAsJsonAsync($"/api/workspaces/{workspaceId ?? person.WorkspaceId}/conversations", new { });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ConversationDto>();
    }

    public static async Task<AnswerDto> AskAsync(this Person person, Guid conversationId, string question, Guid? workspaceId = null)
    {
        var response = await person.Http.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId ?? person.WorkspaceId}/conversations/{conversationId}/messages", new { question });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AnswerDto>();
    }
}
