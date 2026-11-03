using System.Net;
using System.Net.Http.Json;
using KnowledgeBase.IntegrationTests.Support;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Registering_signs_in_and_comes_with_a_workspace_to_own()
    {
        using var anonymous = factory.CreateClient();
        var email = $"Zeynep.{Guid.NewGuid():N}@Example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { email, displayName = " Zeynep Kaya ", password = TestApi.Password, workspaceName = "Destek ekibi" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.ReadAsync<SessionDto>();
        Assert.Equal(email.ToLowerInvariant(), session.User.Email);
        Assert.Equal("Zeynep Kaya", session.User.DisplayName);

        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        var me = await (await anonymous.GetAsync("/api/me")).ReadAsync<MeDto>();
        var workspace = Assert.Single(me.Workspaces);
        Assert.Equal(("Destek ekibi", "Owner", 1, 0), (workspace.Name, workspace.Role, workspace.Members, workspace.Documents));
    }

    [Fact]
    public async Task An_email_is_registered_once_whatever_its_case()
    {
        using var anonymous = factory.CreateClient();
        var email = $"mehmet.{Guid.NewGuid():N}@example.com";
        (await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = "Mehmet", password = TestApi.Password })).EnsureSuccessStatusCode();

        var again = await anonymous.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), displayName = "Mehmet", password = TestApi.Password });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invalid_registrations_say_what_is_wrong()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", displayName = "A", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"email\"", body, StringComparison.Ordinal);
        Assert.Contains("\"displayName\"", body, StringComparison.Ordinal);
        Assert.Contains("\"password\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_works_with_the_right_password_and_says_the_same_for_any_mistake()
    {
        using var person = await factory.RegisterAsync();
        using var anonymous = factory.CreateClient();

        var ok = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = person.Email, password = TestApi.Password });
        var wrongPassword = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = person.Email, password = "wrong-password-123" });
        var unknown = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = TestApi.Password });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync().ContinueWith(t => t.Result.Split("traceId")[0]),
            await unknown.Content.ReadAsStringAsync().ContinueWith(t => t.Result.Split("traceId")[0]));
    }

    [Fact]
    public async Task Everything_but_signing_in_needs_a_token()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/workspaces/{Guid.NewGuid()}/documents")).StatusCode);
    }

    [Fact]
    public async Task Health_and_the_api_reference_are_public()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/openapi/v1.json")).StatusCode);
        var response = await anonymous.GetAsync("/health/live");
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }
}
