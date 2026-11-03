using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector.Npgsql;

namespace KnowledgeBase.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKnowledgeBasePersistence(this IServiceCollection services, string connectionString)
    {
        // One data source for EF Core and for the SQL that reads and writes vectors.
        var dataSource = BuildDataSource(connectionString);
        services.AddSingleton(dataSource);
        services.AddDbContext<KnowledgeBaseDbContext>(options => ConfigureDbContext(options, dataSource));
        return services;
    }

    public static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    }

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>().Database.MigrateAsync(ct);

        // The migration may have just created the vector extension; connections opened before it do not know the type.
        await using var connection = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await connection.ReloadTypesAsync();
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
