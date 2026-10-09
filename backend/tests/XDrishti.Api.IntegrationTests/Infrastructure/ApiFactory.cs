using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API in memory against a throw-away TimescaleDB container (same image as production).
/// Shared by all tests in the collection: one container per test run.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TimescaleImage = "docker.io/timescale/timescaledb:2.30.2-pg18";

    private readonly PostgreSqlContainer _database;

    public ApiFactory()
    {
        ContainerRuntime.UsePodmanIfNeeded();
        _database = new PostgreSqlBuilder(TimescaleImage)
            .WithDatabase("xdrishti")
            .WithUsername("xd_owner")
            .WithPassword("integration-tests")
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        await DatabaseMigrator.MigrateAsync(Services);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _database.GetConnectionString());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestSuite : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
