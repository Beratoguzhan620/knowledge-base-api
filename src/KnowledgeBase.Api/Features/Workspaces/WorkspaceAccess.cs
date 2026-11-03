using System.Security.Claims;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Features.Workspaces;

/// <summary>
/// The one gate in front of everything in a workspace. Membership and role are read from the database on every
/// request, so a member who is removed or demoted loses access on their next click.
/// </summary>
internal static class WorkspaceAccess
{
    public static async Task<Membership> RequireAsync(
        KnowledgeBaseDbContext db, Guid workspaceId, ClaimsPrincipal user, Role needed, CancellationToken ct)
    {
        var userId = user.UserId();
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct)
            // Not "forbidden": whether a workspace with this id exists is none of an outsider's business.
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no workspace with this id among yours.");

        if (!membership.Can(needed))
        {
            throw new DomainException(ErrorCodes.Forbidden, needed switch
            {
                Role.Owner => "Only the workspace's owners can do this.",
                _ => "Only editors and owners can change the documents of this workspace.",
            });
        }

        return membership;
    }

    /// <summary>
    /// Locks the workspace row for the rest of the transaction, so two changes to its members (two owners
    /// demoting each other, say) are checked one after the other and cannot leave it without an owner.
    /// </summary>
    public static Task LockAsync(KnowledgeBaseDbContext db, Guid workspaceId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM workspaces WHERE id = {workspaceId} FOR UPDATE", ct);
}
