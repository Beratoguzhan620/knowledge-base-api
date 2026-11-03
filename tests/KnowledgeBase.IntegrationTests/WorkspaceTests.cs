using System.Net;
using System.Net.Http.Json;
using KnowledgeBase.IntegrationTests.Support;

namespace KnowledgeBase.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class WorkspaceTests(ApiFactory factory)
{
    [Fact]
    public async Task People_create_more_workspaces_and_see_only_their_own()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");

        var created = await ayse.Http.PostAsJsonAsync("/api/workspaces", new { name = "Ürün dokümanları" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workspace = await created.ReadAsync<WorkspaceDto>();

        var mine = await (await ayse.Http.GetAsync("/api/workspaces")).ReadAsync<List<WorkspaceDto>>();
        var his = await (await mehmet.Http.GetAsync("/api/workspaces")).ReadAsync<List<WorkspaceDto>>();
        Assert.Equal(2, mine.Count);
        Assert.Contains(mine, w => w.Id == workspace.Id && w.Role == "Owner");
        Assert.DoesNotContain(his, w => w.Id == workspace.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await mehmet.Http.GetAsync($"/api/workspaces/{workspace.Id}")).StatusCode);
    }

    [Fact]
    public async Task Only_owners_rename_and_delete_and_deleting_takes_everything_with_it()
    {
        using var owner = await factory.RegisterAsync("Owner");
        using var editor = await factory.RegisterAsync("Editor");
        await owner.AddAsync(editor, "Editor");
        var document = await owner.AddDocumentAsync("notes.md", "# Notes\n\nSome text to keep.");

        var editorRename = await editor.Http.PatchAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}", new { name = "Taken over" });
        var ownerRename = await owner.Http.PatchAsJsonAsync($"/api/workspaces/{owner.WorkspaceId}", new { name = "Kargo SSS" });
        var editorDelete = await editor.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}");

        Assert.Equal(HttpStatusCode.Forbidden, editorRename.StatusCode);
        Assert.Equal("Kargo SSS", (await ownerRename.ReadAsync<WorkspaceDto>()).Name);
        Assert.Equal(HttpStatusCode.Forbidden, editorDelete.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/workspaces/{owner.WorkspaceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/workspaces/{owner.WorkspaceId}")).StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM chunks WHERE document_id = @p0", document.Id));
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM document_files WHERE document_id = @p0", document.Id));
    }
}
