using System.Net;
using System.Net.Http.Json;
using KnowledgeBase.IntegrationTests.Support;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MemberTests(ApiFactory factory)
{
    [Fact]
    public async Task Owners_add_people_with_an_account_and_a_role()
    {
        using var owner = await factory.RegisterAsync("Ayşe");
        using var reader = await factory.RegisterAsync("Can");

        var added = await owner.Http.PostAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}/members", new { email = reader.Email.ToUpperInvariant(), role = "Reader" });
        var again = await owner.Http.PostAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}/members", new { email = reader.Email, role = "Editor" });
        var stranger = await owner.Http.PostAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}/members", new { email = "nobody@example.com", role = "Reader" });

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        var members = await (await reader.Http.GetAsync($"/api/workspaces/{owner.WorkspaceId}/members")).ReadAsync<List<MemberDto>>();
        Assert.Equal([("Ayşe", "Owner"), ("Can", "Reader")], members.Select(m => (m.DisplayName, m.Role)));
    }

    [Fact]
    public async Task Readers_read_editors_also_add_and_delete_documents()
    {
        using var owner = await factory.RegisterAsync();
        using var reader = await factory.RegisterAsync();
        using var editor = await factory.RegisterAsync();
        await owner.AddAsync(reader, "Reader");
        await owner.AddAsync(editor, "Editor");

        var byReader = await reader.UploadAsync("reader.md", "# By a reader", owner.WorkspaceId);
        var document = await editor.AddDocumentAsync("editor.md", "# By an editor\n\nText.", owner.WorkspaceId);

        Assert.Equal(HttpStatusCode.Forbidden, byReader.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.Http.GetAsync($"/api/workspaces/{owner.WorkspaceId}/documents/{document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}/documents/{document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Http.PostAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}/members", new { email = reader.Email, role = "Owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}/documents/{document.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_removed_or_demoted_member_loses_access_on_the_next_request()
    {
        using var owner = await factory.RegisterAsync();
        using var member = await factory.RegisterAsync();
        await owner.AddAsync(member, "Editor");
        var documents = $"/api/workspaces/{owner.WorkspaceId}/documents";

        Assert.Equal(HttpStatusCode.OK, (await member.Http.GetAsync(documents)).StatusCode);
        (await owner.Http.PatchAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}/members/{member.Id}", new { role = "Reader" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.UploadAsync("x.md", "# x", owner.WorkspaceId)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}/members/{member.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.Http.GetAsync(documents)).StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave_or_step_down_but_can_after_naming_another()
    {
        using var owner = await factory.RegisterAsync();
        using var colleague = await factory.RegisterAsync();
        await owner.AddAsync(colleague, "Editor");
        var self = $"/api/workspaces/{owner.WorkspaceId}/members/{owner.Id}";

        var leave = await owner.Http.DeleteAsync(self);
        var stepDown = await owner.Http.PatchAsJsonAsync(self, new { role = "Editor" });
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (leave.StatusCode, stepDown.StatusCode));

        (await owner.Http.PatchAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}/members/{colleague.Id}", new { role = "Owner" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync(self)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/workspaces/{owner.WorkspaceId}")).StatusCode);
    }

    [Fact]
    public async Task Two_owners_demoting_each_other_at_once_leave_one_owner()
    {
        using var first = await factory.RegisterAsync();
        using var second = await factory.RegisterAsync();
        await first.AddAsync(second, "Owner");

        var results = await Task.WhenAll(
            first.Http.PatchAsJsonAsync($"/api/workspaces/{first.WorkspaceId}/members/{second.Id}", new { role = "Reader" }),
            second.Http.PatchAsJsonAsync($"/api/workspaces/{first.WorkspaceId}/members/{first.Id}", new { role = "Reader" }));

        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM memberships WHERE workspace_id = @p0 AND role = 'Owner'", first.WorkspaceId));
        Assert.Single(results, r => r.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Readers_can_leave_on_their_own_but_not_remove_others()
    {
        using var owner = await factory.RegisterAsync();
        using var a = await factory.RegisterAsync();
        using var b = await factory.RegisterAsync();
        await owner.AddAsync(a, "Reader");
        await owner.AddAsync(b, "Reader");

        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}/members/{b.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}/members/{a.Id}")).StatusCode);
    }
}
