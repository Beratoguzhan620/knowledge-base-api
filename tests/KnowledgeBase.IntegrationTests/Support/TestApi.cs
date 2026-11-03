using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnowledgeBase.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record WorkspaceDto(Guid Id, string Name, string Role, int Members, int Documents, DateTimeOffset CreatedAt);

public sealed record MeDto(UserDto User, List<WorkspaceDto> Workspaces);

public sealed record MemberDto(Guid UserId, string DisplayName, string Email, string Role, DateTimeOffset JoinedAt);

public sealed record DocumentDto(
    Guid Id, string Title, string FileName, string Format, long SizeBytes, string Language, string Status, string? Error,
    int ChunkCount, int CharacterCount, string? EmbeddingModel, string UploadedBy, DateTimeOffset CreatedAt, DateTimeOffset? IndexedAt);

public sealed record DocumentPageDto(List<DocumentDto> Items, int Total, int Page, int PageSize);

public sealed record ChunkDto(Guid Id, int Ordinal, string? Heading, int? Page, string Text);

public sealed record ChunkPageDto(List<ChunkDto> Items, int Total, int Page, int PageSize);

public sealed record UsageDto(long StorageBytes, long StorageLimitBytes, long MaxFileBytes, int Documents, int Chunks, int Queued, int Indexing, int Ready, int Failed);

/// <summary>A signed-in person with an HTTP client that carries their token, and the workspace they got on sign-up.</summary>
public sealed class Person(ApiFactory factory, SessionDto session, Guid workspaceId) : IDisposable
{
    public HttpClient Http { get; } = Client(factory, session.AccessToken);

    public Guid Id => session.User.Id;

    public string Email => session.User.Email;

    public string Name => session.User.DisplayName;

    /// <summary>The workspace that came with the account.</summary>
    public Guid WorkspaceId { get; } = workspaceId;

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Person> RegisterAsync(this ApiFactory factory, string name = "Ayşe Yılmaz", string? workspaceName = null)
    {
        using var anonymous = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password = Password, workspaceName });
        response.EnsureSuccessStatusCode();
        var session = await response.ReadAsync<SessionDto>();

        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var me = await (await http.GetAsync("/api/me")).ReadAsync<MeDto>();
        return new Person(factory, session, me.Workspaces.Single().Id);
    }

    /// <summary>Adds <paramref name="member"/> to <paramref name="owner"/>'s workspace with a role.</summary>
    public static async Task AddAsync(this Person owner, Person member, string role, Guid? workspaceId = null)
    {
        var response = await owner.Http.PostAsJsonAsync($"/api/workspaces/{workspaceId ?? owner.WorkspaceId}/members", new { email = member.Email, role });
        response.EnsureSuccessStatusCode();
    }

    public static Task<HttpResponseMessage> UploadAsync(this Person person, string fileName, string content, Guid? workspaceId = null, string? title = null) =>
        person.UploadAsync(fileName, Encoding.UTF8.GetBytes(content), workspaceId, title);

    public static async Task<HttpResponseMessage> UploadAsync(this Person person, string fileName, byte[] content, Guid? workspaceId = null, string? title = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (title is not null)
        {
            form.Add(new StringContent(title), "title");
        }

        return await person.Http.PostAsync($"/api/workspaces/{workspaceId ?? person.WorkspaceId}/documents", form);
    }

    public static async Task<DocumentDto> AddDocumentAsync(this Person person, string fileName, string content, Guid? workspaceId = null)
    {
        var response = await person.UploadAsync(fileName, content, workspaceId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<DocumentDto>();
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
