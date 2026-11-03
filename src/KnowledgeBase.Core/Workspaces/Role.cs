namespace KnowledgeBase.Core.Workspaces;

/// <summary>What a member may do in a workspace. Each role includes everything the ones before it can do.</summary>
public enum Role
{
    /// <summary>Reads documents and asks questions.</summary>
    Reader = 0,

    /// <summary>Also adds and deletes documents.</summary>
    Editor = 1,

    /// <summary>Also manages members, renames and deletes the workspace.</summary>
    Owner = 2,
}
