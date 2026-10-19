using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// A throw-away database on the shared test server, migrated like production, with its own host (so its own connection pool and store): for the tests that change the schema of the research tables (a constraint, a trigger) and so must not touch the
/// database the other tests share. Dropped on disposal.
/// </summary>
internal sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;

    private ScratchDatabase(string name, string adminConnectionString, string connectionString, WebApplicationFactory<Program> host)
    {
        Name = name;
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
        Host = host;
    }

    public string Name { get; }

    /// <summary>The owner's connection to the scratch database.</summary>
    public string ConnectionString { get; }

    public WebApplicationFactory<Program> Host { get; }

    public static async Task<ScratchDatabase> CreateAsync(ApiFactory factory, string name, CancellationToken cancellationToken, bool migrate = true)
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = name, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await Execute(connection, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)", cancellationToken);
            await Execute(connection, $"CREATE DATABASE {name}", cancellationToken);
        }

        var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
        if (migrate)
        {
            await DatabaseMigrator.MigrateAsync(host.Services, cancellationToken);
        }

        return new ScratchDatabase(name, admin.ConnectionString, scratch.ConnectionString, host);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"DROP DATABASE IF EXISTS {Name} WITH (FORCE)", CancellationToken.None);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
