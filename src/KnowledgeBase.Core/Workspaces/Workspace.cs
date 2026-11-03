namespace KnowledgeBase.Core.Workspaces;

/// <summary>A shared space for a team's documents. Everything in it is visible only to its members.</summary>
public sealed class Workspace
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 60;

    private Workspace()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new workspace and its first owner.</summary>
    public static (Workspace Workspace, Membership Owner) Create(string name, Guid ownerId, DateTimeOffset now)
    {
        var workspace = new Workspace { Id = Guid.CreateVersion7(now), Name = CleanName(name), CreatedBy = ownerId, CreatedAt = now };
        return (workspace, Membership.Create(workspace.Id, ownerId, Role.Owner, now));
    }

    public void Rename(string name) => Name = CleanName(name);

    /// <summary>
    /// Checks that a change to the members leaves somebody who can manage the workspace.
    /// <paramref name="ownersAfter"/> is how many owners there would be after the change.
    /// </summary>
    public static void EnsureAnOwnerRemains(int ownersAfter)
    {
        if (ownersAfter < 1)
        {
            throw new DomainException(ErrorCodes.InvalidState,
                "A workspace needs at least one owner. Make someone else an owner first, or delete the workspace.");
        }
    }

    private static string CleanName(string? name)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is >= NameMinLength and <= NameMaxLength
            ? clean
            : throw new DomainException(ErrorCodes.ValidationFailed, $"A workspace name has {NameMinLength} to {NameMaxLength} characters.");
    }
}
