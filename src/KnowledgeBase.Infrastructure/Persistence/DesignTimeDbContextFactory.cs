using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KnowledgeBase.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KnowledgeBaseDbContext>
{
    public KnowledgeBaseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KnowledgeBaseDbContext>();
        DependencyInjection.ConfigureDbContext(options, DependencyInjection.BuildDataSource("Host=localhost;Database=knowledge_base_design"));
        return new KnowledgeBaseDbContext(options.Options);
    }
}
