using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KnowledgeBase.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL with pgvector, shared by the whole collection.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    /// <summary>Small limits, so tests can reach them with small files.</summary>
    public const int MaxFileMegabytes = 1;
    public const int WorkspaceStorageMegabytes = 3;

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public string ConnectionString => _database.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:KnowledgeBase", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:AttemptsPerMinute", "100000");
        builder.UseSetting("Limits:MaxFileMegabytes", MaxFileMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Limits:WorkspaceStorageMegabytes", WorkspaceStorageMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue($"p{i}", parameters[i]);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
