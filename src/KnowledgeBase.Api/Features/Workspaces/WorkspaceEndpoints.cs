using System.Security.Claims;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Features.Workspaces;

public sealed record WorkspaceRequest(string Name);

public sealed record WorkspaceResponse(Guid Id, string Name, Role Role, int Members, int Documents, DateTimeOffset CreatedAt);

internal static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces").WithTags("Workspaces").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Your workspaces");
        group.MapPost("/", Create).WithSummary("Create a workspace").WithDescription("You become its owner.");
        group.MapGet("/{workspaceId:guid}", Get).WithSummary("One of your workspaces");
        group.MapPatch("/{workspaceId:guid}", Rename).WithSummary("Rename a workspace (owners)");
        group.MapDelete("/{workspaceId:guid}", Delete).WithSummary("Delete a workspace and everything in it (owners)");
        return app;
    }

    internal static async Task<List<WorkspaceResponse>> ListForAsync(KnowledgeBaseDbContext db, Guid userId, CancellationToken ct) =>
        (await Query(db, userId, null).ToListAsync(ct)).OrderBy(w => w.Name, StringComparer.CurrentCulture).ToList();

    private static async Task<Ok<List<WorkspaceResponse>>> List(ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListForAsync(db, user.UserId(), ct));

    private static async Task<Created<WorkspaceResponse>> Create(
        WorkspaceRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (workspace, owner) = Workspace.Create(request.Name, user.UserId(), clock.GetUtcNow());
        db.Workspaces.Add(workspace);
        db.Memberships.Add(owner);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/workspaces/{workspace.Id}",
            new WorkspaceResponse(workspace.Id, workspace.Name, Role.Owner, 1, 0, workspace.CreatedAt));
    }

    private static async Task<Ok<WorkspaceResponse>> Get(Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        return TypedResults.Ok(await Query(db, user.UserId(), workspaceId).SingleAsync(ct));
    }

    private static async Task<Ok<WorkspaceResponse>> Rename(
        Guid workspaceId, WorkspaceRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Owner, ct);
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId, ct);
        workspace.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await Query(db, user.UserId(), workspaceId).SingleAsync(ct));
    }

    private static async Task<NoContent> Delete(Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Owner, ct);
        // Documents, chunks, files and memberships go with it (foreign keys cascade).
        await db.Workspaces.Where(w => w.Id == workspaceId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    private static IQueryable<WorkspaceResponse> Query(KnowledgeBaseDbContext db, Guid userId, Guid? workspaceId) =>
        from m in db.Memberships
        join w in db.Workspaces on m.WorkspaceId equals w.Id
        where m.UserId == userId && (workspaceId == null || w.Id == workspaceId)
        select new WorkspaceResponse(
            w.Id, w.Name, m.Role,
            db.Memberships.Count(x => x.WorkspaceId == w.Id),
            db.Documents.Count(d => d.WorkspaceId == w.Id),
            w.CreatedAt);
}
