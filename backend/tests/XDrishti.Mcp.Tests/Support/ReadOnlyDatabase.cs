using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using XDrishti.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Mcp.Tests.Support;

/// <summary>
/// One throw-away TimescaleDB server per fixture, set up like production: the owner is the container's superuser (<c>xd_owner</c>, as in the official image), the runtime role <c>xd_app</c> has the default privileges of <c>deploy/db/init/10-roles.sh</c>, the whole chain is
/// migrated, and (unless <c>provision</c> is false) <c>xd-api migrate</c>'s role step has given the two read-only roles their login. The roles are cluster-wide, so each fixture has its own server.
/// </summary>
public class ReadOnlyDatabase : IAsyncLifetime
{
    public const string Image = "docker.io/timescale/timescaledb:2.30.2-pg18";
    public const string Database = "xdrishti";

    private PostgreSqlContainer? _container;

    public ReadOnlyDatabase()
        : this(provision: true)
    {
    }

    protected ReadOnlyDatabase(bool provision) => Provision = provision;

    public bool Provision { get; }

    public string ReadPassword { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public string SqlPassword { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public string Owner { get; private set; } = string.Empty;

    public string App { get; private set; } = string.Empty;

    public string AppPassword { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    internal CapturedLoggerProvider Logs { get; } = new();

    /// <summary>A connection string for a login of this server (no pooling, so a role change is seen at once).</summary>
    public string As(string role, string password) => new NpgsqlConnectionStringBuilder(Owner) { Username = role, Password = password, Pooling = false }.ConnectionString;

    public string AsRead => As("xd_mcp_read", ReadPassword);

    public string AsSql => As("xd_mcp_sql", SqlPassword);

    public async ValueTask InitializeAsync()
    {
        ContainerRuntime.UsePodmanIfNeeded();
        _container = new PostgreSqlBuilder(Image).WithDatabase(Database).WithUsername("xd_owner").WithPassword(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))).Build();
        await _container.StartAsync(TestContext.Current.CancellationToken);
        Owner = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString;
        App = new NpgsqlConnectionStringBuilder(Owner) { Username = "xd_app", Password = AppPassword }.ConnectionString;
        await ExecuteAsync(
            Owner,
            $"""
            CREATE ROLE xd_app LOGIN PASSWORD '{AppPassword}';
            GRANT CONNECT ON DATABASE {Database} TO xd_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT USAGE, SELECT ON SEQUENCES TO xd_app;
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            """);
        await using (var host = MigrationHost.Build(Owner, null, null, Logs))
        {
            await using var scope = host.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        if (Provision)
        {
            await ProvisionAsync(ReadPassword, SqlPassword);
        }
    }

    /// <summary>Runs the role step of <c>xd-api migrate</c> with these passwords (null = the file is absent).</summary>
    public async Task<bool> ProvisionAsync(string? readPassword, string? sqlPassword)
    {
        await using var host = MigrationHost.Build(Owner, readPassword, sqlPassword, Logs);
        return await PostMigration.ProvisionReadOnlyRolesAsync(host, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The SQL state of the error a statement raises, or null when it succeeds.</summary>
    public static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await ExecuteAsync(connectionString, sql);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }
}

/// <summary>A server whose roles are NOT given a login by the fixture: for the tests of the provisioning step itself.</summary>
public sealed class UnprovisionedDatabase : ReadOnlyDatabase
{
    public UnprovisionedDatabase()
        : base(provision: false)
    {
    }
}

[CollectionDefinition(Name)]
public sealed class ReadOnlyDatabaseSuite : ICollectionFixture<ReadOnlyDatabase>
{
    public const string Name = "read-only-database";
}
