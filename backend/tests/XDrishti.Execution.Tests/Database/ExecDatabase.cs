using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using XDrishti.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// One dedicated throw-away TimescaleDB server per fixture, set up like production: the owner is the superuser <c>xd_owner</c>, the runtime role <c>xd_app</c> has the default privileges of <c>deploy/db/init/10-roles.sh</c>, the whole chain is migrated and
/// <c>xd-api migrate</c>'s role step has given <c>xd_exec</c> its login. Roles are cluster-wide, so the shared test server is never used.
/// </summary>
public sealed class ExecDatabase : IAsyncLifetime
{
    private const string Image = "docker.io/timescale/timescaledb:2.30.2-pg18";
    private PostgreSqlContainer? _container;

    public string Owner { get; private set; } = string.Empty;

    public string App { get; private set; } = string.Empty;

    public string Exec { get; private set; } = string.Empty;

    public string ExecPassword { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public Guid AccountId { get; } = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        UsePodmanIfNeeded();
        var appPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _container = new PostgreSqlBuilder(Image).WithDatabase("xdrishti").WithUsername("xd_owner").WithPassword(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)))
            .WithLabel("xdrishti.test", "1").WithCreateParameterModifier(p => { p.HostConfig ??= new(); p.HostConfig.Memory = 1536L * 1024 * 1024; }).Build();
        await _container.StartAsync(TestContext.Current.CancellationToken);
        Owner = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString;
        App = new NpgsqlConnectionStringBuilder(Owner) { Username = "xd_app", Password = appPassword, Pooling = false }.ConnectionString;
        Exec = new NpgsqlConnectionStringBuilder(Owner) { Username = "xd_exec", Password = ExecPassword, Pooling = false }.ConnectionString;
        await ExecuteAsync(
            Owner,
            $"""
            CREATE ROLE xd_app LOGIN PASSWORD '{appPassword}';
            GRANT CONNECT ON DATABASE xdrishti TO xd_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT USAGE, SELECT ON SEQUENCES TO xd_app;
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            """);
        await using var host = Host(ExecPassword);
        await using (var scope = host.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        (await PostMigration.ProvisionExecutionRoleAsync(host, TestContext.Current.CancellationToken)).ShouldBeTrue();
        await ExecuteAsync(
            Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{AccountId}', 'exec-test', 'Exec test', 'Dhan', '1000000001', current_date, '#112233', now(), now(), false, false, false, 0)
            """);
    }

    /// <summary>A service provider with what <c>xd-api migrate</c> has when it provisions the role: the owner's context, the configuration and logging.</summary>
    public ServiceProvider Host(string? execPassword)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ExecRole:Password"] = execPassword }).Build());
        services.AddLogging();
        services.AddDbContext<XDrishtiDbContext>(o => o.UseNpgsql(Owner, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")).UseSnakeCaseNamingConvention());
        return services.BuildServiceProvider();
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

    private static void UsePodmanIfNeeded()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("podman", "machine inspect --format {{.ConnectionInfo.PodmanSocket.Path}}") { RedirectStandardOutput = true, RedirectStandardError = true });
            var path = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(5000);
            if (process is { ExitCode: 0 } && path is not null && File.Exists(path))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", "unix://" + path);
                Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // podman not installed: Testcontainers falls back to its defaults
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ExecDatabaseSuite : ICollectionFixture<ExecDatabase>
{
    public const string Name = "exec-database";
}
