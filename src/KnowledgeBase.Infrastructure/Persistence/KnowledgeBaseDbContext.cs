using KnowledgeBase.Core.Conversations;
using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Users;
using KnowledgeBase.Core.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Infrastructure.Persistence;

/// <summary>
/// Accounts and workspaces, and everything that belongs to a workspace. Chunks also have two columns EF Core
/// does not map: <c>embedding</c> (pgvector) and <c>search</c> (a generated tsvector); the search code
/// reads them with SQL.
/// </summary>
public sealed class KnowledgeBaseDbContext(DbContextOptions<KnowledgeBaseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentFile> DocumentFiles => Set<DocumentFile>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnowledgeBaseDbContext).Assembly);
    }
}
