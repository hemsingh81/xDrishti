using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp.Tests.Support;

/// <summary>The real <c>xd-mcp</c> host (the same wiring as production) on an in-memory server, with the database ports replaced by fakes unless a test asks for the real ones.</summary>
internal sealed class TestMcpHost : IAsyncDisposable
{
    public const string ApiToken = "api-token-for-tests-0123456789";
    public const string OtherToken = "other-token-for-tests-9876543210";

    private TestMcpHost(WebApplication app, FakeContext context, FakeCatalog catalog, FakeSql sql, FakeTimeProvider time)
    {
        App = app;
        Context = context;
        Catalog = catalog;
        Sql = sql;
        Time = time;
    }

    public WebApplication App { get; }

    public FakeContext Context { get; }

    public FakeCatalog Catalog { get; }

    public FakeSql Sql { get; }

    public FakeTimeProvider Time { get; }

    public HttpClient CreateClient() => App.GetTestClient();

    /// <summary>The address Kestrel listens on (only for a host started with <c>kestrel: true</c>).</summary>
    public string Address => App.Urls.First();

    /// <summary>Starts the host. With <paramref name="database"/> the database ports are the real ones over the two read-only logins of that server; without it they are fakes.</summary>
    public static async Task<TestMcpHost> StartAsync(Action<IConfigurationBuilder>? configure = null, FakeContext? context = null, FakeSql? sql = null, ReadOnlyDatabase? database = null, bool kestrel = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        if (kestrel)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["urls"] = "http://127.0.0.1:0" });
        }
        else
        {
            builder.WebHost.UseTestServer();
        }

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "xd-mcp",
            ["Database:ReadConnectionString"] = "Host=unused;Username=xd_mcp_read",
            ["Database:SqlConnectionString"] = "Host=unused;Username=xd_mcp_sql",
            ["Mcp:Tokens:Api"] = ApiToken,
            ["Mcp:Tokens:Other"] = OtherToken,
        });
        if (database is not null)
        {
            var read = new NpgsqlConnectionStringBuilder(database.AsRead);
            var sqlLogin = new NpgsqlConnectionStringBuilder(database.AsSql);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ReadConnectionString"] = $"Host={read.Host};Port={read.Port};Database={read.Database};Username={read.Username}",
                ["Database:SqlConnectionString"] = $"Host={sqlLogin.Host};Port={sqlLogin.Port};Database={sqlLogin.Database};Username={sqlLogin.Username}",
                ["Database:ReadPassword"] = read.Password,
                ["Database:SqlPassword"] = sqlLogin.Password,
            });
        }

        configure?.Invoke(builder.Configuration);
        McpHost.Configure(builder);
        var fakeContext = context ?? new FakeContext();
        var catalog = new FakeCatalog();
        var fakeSql = sql ?? new FakeSql();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero));
        if (database is null)
        {
            builder.Services.RemoveAll<IAiContext>().AddSingleton<IAiContext>(fakeContext);
            builder.Services.RemoveAll<IAiCatalog>().AddSingleton<IAiCatalog>(catalog);
            builder.Services.RemoveAll<IAiSqlRunner>().AddSingleton<IAiSqlRunner>(fakeSql);
            builder.Services.RemoveAll<IAiLabValidator>().AddSingleton<IAiLabValidator>(new FakeLab());
        }

        builder.Services.RemoveAll<TimeProvider>().AddSingleton<TimeProvider>(time);
        var app = builder.Build();
        McpHost.Use(app);
        await app.StartAsync();
        return new TestMcpHost(app, fakeContext, catalog, fakeSql, time);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
