using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Testcontainers.PostgreSql;
using XDrishti.Api.Auth;
using XDrishti.Application.Auth;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API in memory against a throw-away TimescaleDB container (same image as production).
/// Shared by all tests in the collection: one container per test run.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TimescaleImage = "docker.io/timescale/timescaledb:2.30.2-pg18";

    /// <summary>Not a real credential: a made-up address and a password generated anew for every test run.</summary>
    public const string OwnerEmail = "owner@example.invalid";

    public const string OwnerName = "Test Owner";

    private static int _clientCounter;

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

    public string OwnerPassword { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public AdjustableTimeProvider Clock { get; } = new();

    /// <summary>The broker vault key of this test run (random, never a real one).</summary>
    public string VaultKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Every log event the API wrote during the run, rendered with all its properties.</summary>
    public CapturedLogs Logs { get; } = new();

    /// <summary>A fake Dhan server: the API's broker HTTP client never reaches the internet in tests.</summary>
    public FakeBrokers Brokers { get; } = new();

    /// <summary>Superuser (xd_owner) connection to the throw-away server, for tests that need their own database or roles.</summary>
    public string AdminConnectionString => _database.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        await DatabaseMigrator.MigrateAsync(Services);
        (await OwnerBootstrap.ProvisionAsync(Services)).ShouldBe(0);
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
        builder.UseSetting("BrokerVault:Key", VaultKey);
        builder.UseSetting("Auth:Owner:Email", OwnerEmail);
        builder.UseSetting("Auth:Owner:Name", OwnerName);
        builder.UseSetting("Auth:Owner:Password", OwnerPassword);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddTransient<IStartupFilter, LoopbackRemoteAddressStartupFilter>();
            services.AddSingleton<ILogEventSink>(Logs);
            Brokers.Clock = Clock;

            // Only the innermost handler is replaced: the allow-list guard and the client settings stay as in production.
            services.AddHttpClient(BrokerHttpClientNames.Dhan).ConfigurePrimaryHttpMessageHandler(Brokers.CreateHandler);
        });
    }

    /// <summary>A client without a session. Each client appears to come from its own address (own rate-limit bucket); cookies are handled by the tests.</summary>
    public HttpClient CreateAnonymousClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        var n = Interlocked.Increment(ref _clientCounter);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.18.{n / 250}.{(n % 250) + 1}");
        return client;
    }

    /// <summary>Signs in as the owner and returns a client that sends the session cookie and the CSRF token.</summary>
    public async Task<SignedInClient> SignInAsync(CancellationToken cancellationToken)
    {
        var client = CreateAnonymousClient();
        using var response = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new { email = OwnerEmail, password = OwnerPassword }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var session = (await response.Content.ReadFromJsonAsync<SessionInfo>(cancellationToken))!;
        var cookie = SessionCookieValue(response)!;
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={cookie}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        return new SignedInClient(client, session, cookie);
    }

    /// <summary>Signs in and re-enters the password, so that sensitive endpoints are allowed for the next five minutes.</summary>
    public async Task<SignedInClient> SignInElevatedAsync(CancellationToken cancellationToken)
    {
        var signedIn = await SignInAsync(cancellationToken);
        using var response = await signedIn.Client.StepUpAsync(OwnerPassword, cancellationToken);
        response.EnsureSuccessStatusCode();
        return signedIn;
    }

    /// <summary>Removes every account (its secrets go with it), its audit trail, and the profiles and buckets the tests added, so a test starts from the seeded state.</summary>
    public async Task ResetAccountsAsync()
    {
        await this.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM accounts.account_events;
                DELETE FROM accounts.broker_accounts;
                DELETE FROM accounts.risk_profiles WHERE NOT is_seeded;
                DELETE FROM accounts.portfolio_buckets WHERE name NOT IN ('Trading', 'MyLongTerm', 'MyChoice');
                """);
            return 0;
        });
    }

    /// <summary>Resets the owner to the factory's credentials: clears a lockout and ends every session. For tests that lock or change the account.</summary>
    public async Task RestoreOwnerAsync() => (await OwnerBootstrap.ResetAsync(Services)).ShouldBe(0);

    public static string? SessionCookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var line = cookies.FirstOrDefault(c => c.StartsWith("xd.sid=", StringComparison.Ordinal));
        return line?.Split(';')[0]["xd.sid=".Length..];
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestSuite : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
