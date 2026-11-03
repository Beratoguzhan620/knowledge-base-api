namespace KnowledgeBase.Core.Workspaces;

public sealed class Membership
{
    private Membership()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static Membership Create(Guid workspaceId, Guid userId, Role role, DateTimeOffset now) =>
        new() { WorkspaceId = workspaceId, UserId = userId, Role = Enum.IsDefined(role) ? role : throw Invalid(), JoinedAt = now };

    public bool Can(Role needed) => Role >= needed;

    public void ChangeRole(Role role) => Role = Enum.IsDefined(role) ? role : throw Invalid();

    private static DomainException Invalid() =>
        new(ErrorCodes.ValidationFailed, "A role is Reader, Editor or Owner.");
}
