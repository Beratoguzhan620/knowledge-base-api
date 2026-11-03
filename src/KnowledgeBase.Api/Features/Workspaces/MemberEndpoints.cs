using System.Security.Claims;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Api.Features.Auth;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Users;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Features.Workspaces;

public sealed record AddMemberRequest(string Email, Role Role);

public sealed record ChangeRoleRequest(Role Role);

public sealed record MemberResponse(Guid UserId, string DisplayName, string Email, Role Role, DateTimeOffset JoinedAt);

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/{workspaceId:guid}/members").WithTags("Members").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The workspace's members");
        group.MapPost("/", Add).WithSummary("Add someone who has an account (owners)")
            .WithDescription("`role` is `Reader`, `Editor` or `Owner`.");
        group.MapPatch("/{userId:guid}", ChangeRole).WithSummary("Change a member's role (owners)");
        group.MapDelete("/{userId:guid}", Remove).WithSummary("Remove a member (owners), or leave (yourself)")
            .WithDescription("A workspace always keeps at least one owner.");
        return app;
    }

    private static async Task<Ok<List<MemberResponse>>> List(Guid workspaceId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Reader, ct);
        var members = await Members(db, workspaceId, null).ToListAsync(ct);
        return TypedResults.Ok(members.OrderByDescending(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture).ToList());
    }

    private static async Task<Results<Created<MemberResponse>, ValidationProblem>> Add(
        Guid workspaceId, AddMemberRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Owner, ct);
        var validation = new Validation()
            .Require(AuthEndpoints.IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(Enum.IsDefined(request.Role), "role", "A role is Reader, Editor or Owner.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var invitee = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Nobody has an account with this email yet. Ask them to sign up, then add them.");
        if (await db.Memberships.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == invitee.Id, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already a member.");
        }

        var membership = Membership.Create(workspaceId, invitee.Id, request.Role, clock.GetUtcNow());
        db.Memberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already a member.");
        }

        return TypedResults.Created($"/api/workspaces/{workspaceId}/members/{invitee.Id}",
            new MemberResponse(invitee.Id, invitee.DisplayName, invitee.Email, membership.Role, membership.JoinedAt));
    }

    private static async Task<Ok<MemberResponse>> ChangeRole(
        Guid workspaceId, Guid userId, ChangeRoleRequest request, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await WorkspaceAccess.LockAsync(db, workspaceId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not a member of the workspace.");

        var owners = await db.Memberships.CountAsync(m => m.WorkspaceId == workspaceId && m.Role == Role.Owner, ct);
        var wasOwner = membership.Role == Role.Owner;
        membership.ChangeRole(request.Role);
        Workspace.EnsureAnOwnerRemains(owners - (wasOwner ? 1 : 0) + (membership.Role == Role.Owner ? 1 : 0));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await Members(db, workspaceId, userId).SingleAsync(ct));
    }

    private static async Task<NoContent> Remove(
        Guid workspaceId, Guid userId, ClaimsPrincipal user, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        // Anyone may leave; removing someone else takes an owner.
        await WorkspaceAccess.RequireAsync(db, workspaceId, user, userId == user.UserId() ? Role.Reader : Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await WorkspaceAccess.LockAsync(db, workspaceId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not a member of the workspace.");
        if (membership.Role == Role.Owner)
        {
            var owners = await db.Memberships.CountAsync(m => m.WorkspaceId == workspaceId && m.Role == Role.Owner, ct);
            Workspace.EnsureAnOwnerRemains(owners - 1);
        }

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static IQueryable<MemberResponse> Members(KnowledgeBaseDbContext db, Guid workspaceId, Guid? userId) =>
        from m in db.Memberships
        join u in db.Users on m.UserId equals u.Id
        where m.WorkspaceId == workspaceId && (userId == null || m.UserId == userId)
        select new MemberResponse(u.Id, u.DisplayName, u.Email, m.Role, m.JoinedAt);
}
