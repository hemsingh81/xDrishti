using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;
using Testcontainers.PostgreSql;
using XDrishti.Api.Auth;
using XDrishti.Api.Platform;
using XDrishti.Application;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Auth;
using XDrishti.Application.Live.Push;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Market;
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

    /// <summary>Fake Dhan and NSE file servers: the API's master-data HTTP clients never reach the internet in tests.</summary>
    public FakeMasterData MasterData { get; } = new();

    /// <summary>Registered instrument refreshes wait here until a test runs them.</summary>
    public ManualRefreshJobs RefreshJobs { get; } = new();

    /// <summary>Registered data jobs wait here until a test runs them.</summary>
    public ManualDataJobs DataJobs { get; } = new();

    /// <summary>The folder the API reads CSV files from (<c>Data:CsvFolder</c>): a private temporary folder, emptied by <see cref="ResetInstrumentsAsync"/>.</summary>
    public string CsvFolder { get; } = Directory.CreateTempSubdirectory("xd-csv-").FullName;

    /// <summary>Stands in for the Dhan Data API and the Data account when a test sets them; otherwise the real placeholders answer.</summary>
    public FakeDhan Dhan { get; } = new();

    /// <summary>Lets a test change the data "meanwhile", inside the trade use cases' unit of work (see <see cref="TradeHooks"/>).</summary>
    public TradeHooks TradeHooks { get; } = new();

    /// <summary>Dhan's market-data socket: the live feed's engine (registered here as in the xd-feed host) connects to this one.</summary>
    public FakeFeedSocket FeedSocket { get; } = new();

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
        if (Directory.Exists(CsvFolder))
        {
            Directory.Delete(CsvFolder, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _database.GetConnectionString());
        builder.UseSetting("BrokerVault:Key", VaultKey);
        builder.UseSetting("Instruments:AutoRefresh:Enabled", "false"); // tests start refreshes themselves
        builder.UseSetting("Portfolio:AutoRefresh:Enabled", "false"); // tests drive the holdings schedule themselves
        builder.UseSetting("Data:Eod:Enabled", "false"); // first-start seed (ADR 0018): the end-of-day service starts paused; tests resume it and ask the scheduler themselves, with the clock pinned
        builder.UseSetting("Portfolio:BrokerHistory:Enabled", "false"); // first-start seed (ADR 0018): the trades and ledger service starts paused; tests use Sync now or run it themselves
        builder.UseSetting("Portfolio:BrokerHistory:CallSpacingMs", "0"); // no 250 ms between the calls to the fake Dhan
        builder.UseSetting("StrategyLab:RunWorker", "false"); // Lab runs are driven by the tests through LabRunDispatcher.DrainAsync
        builder.UseSetting("Data:CsvFolder", CsvFolder);
        builder.UseSetting("Auth:Owner:Email", OwnerEmail);
        builder.UseSetting("Auth:Owner:Name", OwnerName);
        builder.UseSetting("Auth:Owner:Password", OwnerPassword);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddTransient<IStartupFilter, LoopbackRemoteAddressStartupFilter>();
            services.AddSingleton<ILogEventSink>(Logs);
            Brokers.Clock = Clock;

            // The registered ticket-notice source stays the inner one; the switch lets a test play P7-20's items.
            var ticketSource = services.Single(d => d.ServiceType == typeof(ITicketNoticeSource));
            services.Remove(ticketSource);
            services.AddSingleton<TicketNoticeSwitch>(sp => new TicketNoticeSwitch((ITicketNoticeSource)ActivatorUtilities.CreateInstance(sp, ticketSource.ImplementationType!)));
            services.AddScoped<ITicketNoticeSource>(sp => sp.GetRequiredService<TicketNoticeSwitch>());

            // Only the innermost handler is replaced: the allow-list guard and the client settings stay as in production.
            services.AddHttpClient(BrokerHttpClientNames.Dhan).ConfigurePrimaryHttpMessageHandler(Brokers.CreateHandler);
            services.AddHttpClient(BrokerHttpClientNames.DhanHoldings).ConfigurePrimaryHttpMessageHandler(Brokers.CreateHandler);
            services.AddHttpClient(BrokerHttpClientNames.DhanChart).ConfigurePrimaryHttpMessageHandler(Brokers.CreateHandler);
            services.AddHttpClient(BrokerHttpClientNames.DhanQuote).ConfigurePrimaryHttpMessageHandler(Brokers.CreateHandler);
            services.AddHttpClient(BrokerHttpClientNames.DhanHistory).ConfigurePrimaryHttpMessageHandler(Brokers.CreateHandler);

            // Same for the instrument master's two file servers and NSE's holiday list; refreshes are run by the tests, not by the background worker.
            services.AddHttpClient(MasterDataHttpClientNames.Dhan).ConfigurePrimaryHttpMessageHandler(MasterData.CreateHandler);
            services.AddHttpClient(MasterDataHttpClientNames.Nse).ConfigurePrimaryHttpMessageHandler(MasterData.CreateHandler);
            services.AddHttpClient(MasterDataHttpClientNames.NseApi).ConfigurePrimaryHttpMessageHandler(MasterData.CreateHandler);
            services.AddSingleton<IRefreshJobChannel>(RefreshJobs);
            services.AddSingleton<IDataJobChannel>(DataJobs);

            // The scheduler's own loop is switched off for the suite: tests look at the schedules themselves (ServiceScheduler.TickAsync) with the clock pinned.
            services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ServiceSchedulerHost)));

            // The Dhan Data API source and the Data account: the real ones answer (over the fake Dhan server above) unless a test puts a fake in (FakeDhan).
            Dhan.Wrap(services);

            // The live feed's adapters and engine run in the xd-feed host; here they run over the real database with the Dhan socket replaced, and the flush fast enough for a test to wait for.
            services.AddLiveFeed();
            services.AddFeedEngine();
            services.AddSingleton(LiveFeedSettings.Default with { FlushInterval = TimeSpan.FromMilliseconds(200), SubscribeSpacing = TimeSpan.Zero });
            services.AddSingleton<ILiveFeedConnector>(FeedSocket);

            // The SignalR hub's push runs in this host (as in xd-api): its waits are shortened so that a test does not wait seconds for a push; the sweep stays at its 30 seconds, so a push in a test comes from a notification.
            services.AddSingleton(PushSettings.Default with
            {
                MinPushInterval = TimeSpan.FromMilliseconds(100),
                StatusPollInterval = TimeSpan.FromMilliseconds(200),
                StatusRefreshInterval = TimeSpan.FromSeconds(2),
                SessionCheckInterval = TimeSpan.FromMilliseconds(300),
            });

            // The trade use cases read the applied corporate actions inside their unit of work: a test can act at that moment (TradeHooks).
            var reference = services.Single(d => d.ServiceType == typeof(ITradeReferenceData));
            services.Remove(reference);
            services.AddScoped<ITradeReferenceData>(sp => new HookedTradeReferenceData((ITradeReferenceData)ActivatorUtilities.CreateInstance(sp, reference.ImplementationType!), TradeHooks));
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

    /// <summary>Removes every account (its secrets go with it), its holdings and syncs, its audit trail, and the buckets and profiles the tests added, so a test starts from the seeded state (two risk profiles, no buckets).</summary>
    public async Task ResetAccountsAsync()
    {
        await this.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM paper.exits;
                DELETE FROM paper.entries;
                DELETE FROM paper.skips;
                DELETE FROM paper.capture_runs;
                DELETE FROM paper.epoch_notes;
                DELETE FROM paper.config_changes;
                DELETE FROM paper.config_snapshots;
                DELETE FROM paper.epoch_accounts;
                DELETE FROM paper.epochs;
                DELETE FROM trading.ticket_notice_acks;
                DELETE FROM trading.ticket_prompts;
                DELETE FROM trading.ticket_alerts;
                DELETE FROM trading.fill_grades;
                DELETE FROM trading.journal_fills;
                DELETE FROM trading.journal_entries;
                DELETE FROM trading.plan_events;
                DELETE FROM workflow.approvals;
                DELETE FROM workflow.autonomy_settings;
                DELETE FROM workflow.suggestion_seen;
                DELETE FROM workflow.suggestions;
                DELETE FROM trading.plan_item_allocations;
                DELETE FROM trading.plan_runs;
                DELETE FROM trading.grades;
                DELETE FROM trading.plan_items;
                DELETE FROM trading.plans;
                DELETE FROM portfolio.price_alerts;
                DELETE FROM portfolio.action_states;
                DELETE FROM portfolio.dividend_suggestions;
                DELETE FROM portfolio.dividends;
                DELETE FROM portfolio.dividend_batches;
                DELETE FROM portfolio.dividend_aliases;
                DELETE FROM portfolio.ledger_entries;
                DELETE FROM portfolio.cost_batches;
                DELETE FROM portfolio.security_aliases;
                DELETE FROM portfolio.transactions;
                DELETE FROM portfolio.broker_sync_windows;
                DELETE FROM portfolio.attention_items;
                DELETE FROM portfolio.broker_sync_state;
                DELETE FROM portfolio.broker_sync_runs;
                DELETE FROM portfolio.trade_batches;
                DELETE FROM portfolio.holding_lots;
                DELETE FROM portfolio.holdings;
                DELETE FROM portfolio.holding_syncs;
                DELETE FROM accounts.account_events;
                DELETE FROM accounts.report_client_codes;
                UPDATE accounts.portfolio_settings SET default_bucket_id = NULL;
                DELETE FROM accounts.portfolio_buckets;
                DELETE FROM accounts.broker_accounts;
                DELETE FROM accounts.risk_profiles WHERE NOT is_seeded;
                """);
            return 0;
        });
        Services.GetRequiredService<IPortfolioHistoryCache>().Invalidate();
    }

    /// <summary>Resets the accounts side and the instrument side (which also removes the stored service schedules), so a test starts from the seeded state whatever ran before it.</summary>
    public async Task ResetAllAsync()
    {
        await ResetAccountsAsync();
        await ResetInstrumentsAsync();
    }

    /// <summary>
    /// Throws when a table that a reset leaves empty holds rows, naming the table: a class that ran earlier leaked data, and the failure of the test that finds it would
    /// otherwise point at the wrong place.
    /// </summary>
    public async Task AssertCleanAsync()
    {
        string[] tables = ["accounts.broker_accounts", "portfolio.holdings", "portfolio.attention_items", "platform.service_schedules"];
        foreach (var table in tables)
        {
            var count = await this.WithDbAsync(async db =>
            {
                var connection = db.Database.GetDbConnection();
                await connection.OpenAsync();
                try
                {
                    await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // the table names are the constants above
                    command.CommandText = $"SELECT count(*) FROM {table}";
#pragma warning restore CA2100
                    return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
                }
                finally
                {
                    await connection.CloseAsync();
                }
            });
            if (count != 0)
            {
                throw new InvalidOperationException($"{table} holds {count} row(s) after a reset: an earlier test class leaked data.");
            }
        }
    }

    /// <summary>
    /// Puts the clock back to real time and removes the sign-in audit events that a test wrote while the clock was ahead of real time: they would otherwise
    /// count as the "latest" events for every test that reads the audit trail afterwards.
    /// </summary>
    public async Task ResetClockAsync()
    {
        Clock.Reset();
        await this.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM auth.audit_events WHERE at > now()");
            return 0;
        });
    }

    /// <summary>Removes the stored schedules and the run history of the services (and their audit events), so every service starts on its first-start settings again.</summary>
    public async Task ResetServicesAsync()
    {
        await this.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM platform.service_runs;
                DELETE FROM platform.service_schedules;
                DELETE FROM accounts.account_events WHERE event LIKE 'Service%';
                """);
            return 0;
        });
    }

    /// <summary>
    /// Empties the instrument master (instruments, baskets and their members, tracking, broker ids and refresh records) and the fake file servers, so a
    /// test starts from nothing. The three baskets the migration creates are put back, empty.
    /// </summary>
    public async Task ResetInstrumentsAsync()
    {
        MasterData.Reset();
        RefreshJobs.Clear();
        DataJobs.Clear();
        Dhan.Reset();
        foreach (var file in Directory.EnumerateFileSystemEntries(CsvFolder))
        {
            File.Delete(file);
        }

        await this.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM platform.service_runs;
                DELETE FROM platform.service_schedules;
                DELETE FROM market.data_jobs;
                DELETE FROM market.quality_results;
                DELETE FROM market.quality_runs;
                DELETE FROM market.bar_gaps;
                DELETE FROM platform.app_settings;
                DELETE FROM paper.exits;
                DELETE FROM paper.entries;
                DELETE FROM paper.skips;
                DELETE FROM paper.capture_runs;
                DELETE FROM paper.epoch_notes;
                DELETE FROM paper.config_changes;
                DELETE FROM paper.config_snapshots;
                DELETE FROM paper.epoch_accounts;
                DELETE FROM paper.epochs;
                DELETE FROM trading.ticket_notice_acks;
                DELETE FROM trading.ticket_prompts;
                DELETE FROM trading.ticket_alerts;
                DELETE FROM trading.fill_grades;
                DELETE FROM trading.journal_fills;
                DELETE FROM trading.journal_entries;
                DELETE FROM trading.plan_events;
                DELETE FROM workflow.approvals;
                DELETE FROM workflow.autonomy_settings;
                DELETE FROM workflow.suggestion_seen;
                DELETE FROM workflow.suggestions;
                DELETE FROM trading.plan_item_allocations;
                DELETE FROM trading.plan_runs;
                DELETE FROM trading.grades;
                DELETE FROM trading.plan_items;
                DELETE FROM trading.plans;
                DELETE FROM portfolio.price_alerts;
                DELETE FROM portfolio.action_states;
                DELETE FROM market.corporate_actions;
                DELETE FROM market.trading_days;
                DELETE FROM market.csv_files;
                DELETE FROM market.bars_agg;
                DELETE FROM market.bars_1m;
                DELETE FROM market.bar_coverage;
                DELETE FROM market.basket_members;
                DELETE FROM market.baskets;
                DELETE FROM market.tracked_instruments;
                DELETE FROM accounts.account_events WHERE event LIKE 'Instruments%' OR event LIKE 'Basket%' OR event LIKE 'Calendar%' OR event LIKE 'CorporateAction%' OR event LIKE 'Service%' OR event = 'PortfolioBenchmarkChanged' OR event LIKE 'Action%' OR event LIKE 'PriceAlert%';
                DELETE FROM market.live_quotes;
                DELETE FROM market.instrument_broker_ids;
                DELETE FROM market.instruments;
                DELETE FROM market.master_refreshes;
                """);
            foreach (var basket in DefaultBaskets.All)
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision)
                    VALUES ({basket.Id}, {basket.Name}, {basket.Colour}, {basket.Purpose.ToCode()}, {basket.Position}, now(), 0)
                    """);
            }

            return 0;
        });
        Services.GetRequiredService<IPortfolioHistoryCache>().Invalidate();
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
