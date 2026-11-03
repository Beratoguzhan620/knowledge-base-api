using KnowledgeBase.Core;
using KnowledgeBase.Core.Workspaces;

namespace KnowledgeBase.UnitTests;

public sealed class WorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creating_a_workspace_makes_its_creator_the_owner()
    {
        var userId = Guid.NewGuid();
        var (workspace, owner) = Workspace.Create("  Destek   ekibi ", userId, Now);

        Assert.Equal("Destek ekibi", workspace.Name);
        Assert.Equal((workspace.Id, userId, Role.Owner), (owner.WorkspaceId, owner.UserId, owner.Role));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("This name is far too long for a workspace and goes on and on and on")]
    public void Names_have_a_sensible_length(string name)
    {
        var error = Assert.Throws<DomainException>(() => Workspace.Create(name, Guid.NewGuid(), Now));
        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Theory]
    [InlineData(Role.Reader, Role.Reader, true)]
    [InlineData(Role.Reader, Role.Editor, false)]
    [InlineData(Role.Editor, Role.Editor, true)]
    [InlineData(Role.Editor, Role.Owner, false)]
    [InlineData(Role.Owner, Role.Reader, true)]
    public void Each_role_includes_the_ones_below_it(Role role, Role needed, bool allowed)
    {
        Assert.Equal(allowed, Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Now).Can(needed));
    }

    [Fact]
    public void A_workspace_never_ends_up_without_an_owner()
    {
        Workspace.EnsureAnOwnerRemains(1);
        var error = Assert.Throws<DomainException>(() => Workspace.EnsureAnOwnerRemains(0));
        Assert.Equal(ErrorCodes.InvalidState, error.Code);
    }

    [Fact]
    public void Unknown_roles_are_refused()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), Role.Reader, Now);
        Assert.Throws<DomainException>(() => membership.ChangeRole((Role)7));
    }
}
