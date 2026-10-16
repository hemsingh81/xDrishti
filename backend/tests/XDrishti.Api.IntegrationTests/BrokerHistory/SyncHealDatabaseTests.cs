using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// The tables and statements of migration AddSyncHealCounters (P2-16, ADR 0027) on the real database: the columns and checks, the grants (the runtime role never deletes the health figures, the noted windows or the attention items), the day arithmetic of the failure counters
/// in India time, the runs that are failed as interrupted and noted in the same statement, the mapper version, the earlier-history state, the noted windows, the memory of the attention items, and the migration up and down over a database that holds data.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SyncHealDatabaseTests(ApiFactory factory)
{
    private const string MigrationDatabase = "xd_sync_heal_migration_probe";

    private const string GrantMigrationDatabase = "xd_attention_grant_migration_probe";

    private const string BeforeGrantMigration = "20261015072206_AddReportClientCodeHash";

    private const string BeforeMigration = "20261014205551_AddDividendImportConcurrencyTokens";

    private const string AccountId = "00000000-0000-7000-8000-00000000a201";

    private static readonly Guid Account = Guid.Parse(AccountId);

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE"];

    private static readonly string[] StateKinds = ["holdings", "trades", "ledger", "quotes"];

    private static readonly string[] NewTables = ["broker_sync_state", "broker_sync_windows", "attention_items"];

    /// <summary>The portfolio tables whose rows are kept for good: the runtime role never deletes from them (ADR 0030 gives it DELETE on <c>attention_items</c> only).</summary>
    private static readonly string[] NeverDeleteTables = ["broker_sync_state", "broker_sync_windows", "broker_sync_runs", "ledger_entries", "dividend_batches", "dividend_aliases"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string AccountSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{AccountId}', 'probe-heal', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    private async Task<NpgsqlConnection> ConnectedAccountAsync()
    {
        await factory.ResetAccountsAsync();
        var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);
        return connection;
    }

    private static async Task<string[]> ColumnsAsync(NpgsqlConnection connection, string table)
    {
        var columns = new List<string>();
        await using var command = new NpgsqlCommand($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = '{table}' ORDER BY ordinal_position", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns];
    }

    private static DateTimeOffset At(int day, int hour, int minute, int offsetMinutes = 330) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromMinutes(offsetMinutes));

    // ---- the tables ----

    [Fact]
    public async Task The_new_tables_have_the_columns_of_the_contract_and_the_checks()
    {
        await using var connection = await ConnectedAccountAsync();

        (await ColumnsAsync(connection, "broker_sync_windows")).ShouldBe(["account_id", "kind", "from_date", "to_date", "dropped", "needs_reread", "noted_at"]);
        (await ColumnsAsync(connection, "attention_items")).ShouldBe(["id", "kind", "severity", "title", "text", "actions", "since", "resolved_at", "account_id", "updated_at"]);
        (await AppRoleProbe.ScalarAsync(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'attention_items' AND column_name = 'actions'", Ct)).ShouldBe("jsonb");
        (await AppRoleProbe.ScalarAsync(connection, "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'broker_sync_state' AND column_name = 'watermark_date'", Ct)).ShouldBe("YES");
        foreach (var kind in StateKinds)
        {
            await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.broker_sync_state (account_id, kind, updated_at) VALUES ('{AccountId}', '{kind}', now())", Ct);
        }

        var refusedKind = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.broker_sync_windows (account_id, kind, from_date, to_date, dropped, needs_reread, noted_at) VALUES ('{AccountId}', 'quotes', '2026-10-01', '2026-10-02', 1, true, now())", Ct));
        var refusedSeverity = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.attention_items (id, kind, severity, title, text, actions, since, updated_at) VALUES ('x', 'k', 'fatal', 't', 't', '[]', now(), now())", Ct));
        (refusedKind.ConstraintName, refusedSeverity.ConstraintName).ShouldBe(("ck_broker_sync_windows_kind", "ck_attention_items_severity"));
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_the_health_windows_and_items_and_delete_only_the_items()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);
                await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.broker_sync_state (account_id, kind, updated_at) VALUES ('{AccountId}', 'holdings', now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.broker_sync_windows (account_id, kind, from_date, to_date, dropped, needs_reread, noted_at) VALUES ('{AccountId}', 'trades', '2026-10-01', '2026-10-02', 1, true, now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.attention_items (id, kind, severity, title, text, actions, since, updated_at) VALUES ('x', 'k', 'info', 't', 't', '[]', now(), now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.broker_sync_state SET attempts = 2", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.broker_sync_windows SET needs_reread = false", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.attention_items SET resolved_at = now()", Ct);

                foreach (var table in NewTables)
                {
                    foreach (var privilege in Privileges)
                    {
                        (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', '{privilege}')", Ct)).ShouldBe(true, $"{table} {privilege}");
                    }
                }

                // ADR 0030: the attention items are working memory, so they are the one table here whose rows the runtime role may delete ...
                (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'portfolio.attention_items', 'DELETE')", Ct)).ShouldBe(true);
                // ... and every table that is kept for good still refuses it.
                foreach (var table in NeverDeleteTables)
                {
                    (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', 'DELETE')", Ct)).ShouldBe(false, table);
                    var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, $"DELETE FROM portfolio.{table}", Ct));
                    refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, table);
                }

                await AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.attention_items", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.attention_items", Ct)).ShouldBe(0L);
            },
            Ct);
    }

    // ---- the failure counters ----

    private static SyncHealth Health(IReadOnlyList<SyncHealth> all, SyncHealthKind kind) => all.Single(h => h.Kind == kind);

    [Fact]
    public async Task Failures_are_counted_by_india_day_a_later_one_of_the_same_day_adds_an_attempt_and_the_first_of_a_new_day_adds_a_day()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();

        await store.RecordFailureAsync(Account, SyncHealthKind.Trades, "unavailable", At(12, 8, 30), Ct); // 08:30 IST
        await store.RecordFailureAsync(Account, SyncHealthKind.Trades, "unavailable", At(12, 22, 30), Ct); // 22:30 IST, the same India day
        var sameDay = await store.GetAsync(Account, SyncHealthKind.Trades, Ct);
        await store.RecordFailureAsync(Account, SyncHealthKind.Trades, "rate_limited", At(12, 19, 0, offsetMinutes: 0), Ct); // 19:00 UTC = 00:30 IST the next day, still the same UTC day
        var nextDay = await store.GetAsync(Account, SyncHealthKind.Trades, Ct);

        (sameDay.ConsecutiveFailedDays, sameDay.Attempts, sameDay.LastFailureReason).ShouldBe((1, 2, "unavailable"));
        (nextDay.ConsecutiveFailedDays, nextDay.Attempts, nextDay.LastFailureReason, nextDay.LastFailedAt).ShouldBe((2, 1, "rate_limited", At(12, 19, 0, offsetMinutes: 0)));
        (await store.GetAsync(Account, SyncHealthKind.Ledger, Ct)).ShouldBe(SyncHealth.None(Account, SyncHealthKind.Ledger)); // each kind has its own
    }

    [Fact]
    public async Task A_success_clears_the_counters_and_a_healthy_kind_gets_no_row()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();

        await store.RecordSuccessAsync(Account, SyncHealthKind.Holdings, At(12, 10, 0), Ct);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.broker_sync_state", Ct)).ShouldBe(0L);

        await store.RecordFailureAsync(Account, SyncHealthKind.Holdings, "unavailable", At(12, 10, 0), Ct);
        await store.RecordFailureAsync(Account, SyncHealthKind.Holdings, "unavailable", At(13, 10, 0), Ct);
        (await store.GetAsync(Account, SyncHealthKind.Holdings, Ct)).ConsecutiveFailedDays.ShouldBe(2);
        await store.RecordSuccessAsync(Account, SyncHealthKind.Holdings, At(13, 11, 0), Ct);
        var healthy = await store.GetAsync(Account, SyncHealthKind.Holdings, Ct);
        await store.RecordFailureAsync(Account, SyncHealthKind.Holdings, "unavailable", At(14, 10, 0), Ct);

        (healthy.ConsecutiveFailedDays, healthy.Attempts, healthy.LastFailureReason, healthy.LastFailedAt).ShouldBe((0, 0, null, null));
        (await store.GetAsync(Account, SyncHealthKind.Holdings, Ct)).ConsecutiveFailedDays.ShouldBe(1);
    }

    [Fact]
    public async Task All_kinds_of_the_given_accounts_are_read_in_one_go()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();
        await store.RecordFailureAsync(Account, SyncHealthKind.Holdings, "unavailable", At(12, 10, 0), Ct);
        await store.RecordFailureAsync(Account, SyncHealthKind.Quotes, "rate_limited", At(12, 10, 0), Ct);
        await store.StampMapperVersionAsync(Account, SyncHealthKind.Trades, 1, At(12, 10, 0), Ct);

        var all = await store.ListAsync([Account, Guid.NewGuid()], Ct);

        all.Select(h => h.Kind).Order().ShouldBe([SyncHealthKind.Holdings, SyncHealthKind.Trades, SyncHealthKind.Quotes]);
        (await store.ListAsync([], Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stale_run_is_failed_by_the_next_start_and_noted_as_interrupted_in_the_same_statement()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var brokerRuns = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var holdingSyncs = scope.ServiceProvider.GetRequiredService<IHoldingSyncStore>();
        var health = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();
        var now = DateTimeOffset.UtcNow;
        var old = BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now.AddHours(-2));
        (await brokerRuns.TryStartAsync(old, TimeSpan.FromMinutes(30), now.AddHours(-2), Ct)).ShouldBeTrue();

        (await brokerRuns.TryStartAsync(BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now), TimeSpan.FromMinutes(30), now, Ct)).ShouldBeTrue();

        var trades = await health.GetAsync(Account, SyncHealthKind.Trades, Ct);
        (trades.LastFailureReason, trades.ConsecutiveFailedDays, trades.Attempts).ShouldBe(("interrupted", 1, 1));
        (await AppRoleProbe.ScalarAsync(admin, $"SELECT status FROM portfolio.broker_sync_runs WHERE id = '{old.Id}'", Ct)).ShouldBe("failed");

        await brokerRuns.CompleteAsync((await brokerRuns.GetLastAsync(Account, BrokerSyncKind.Trades, Ct))!.Id, SyncStatus.Succeeded, BrokerSyncCounts.None, null, now, Ct);
        var holdings = HoldingSync.Start(Account, SyncTrigger.Manual, "o", now.AddHours(-1));
        (await holdingSyncs.TryStartAsync(holdings, TimeSpan.FromMinutes(10), now.AddHours(-1), Ct)).ShouldBeTrue();
        (await holdingSyncs.TryStartAsync(HoldingSync.Start(Account, SyncTrigger.Manual, "o", now), TimeSpan.FromMinutes(10), now, Ct)).ShouldBeTrue();

        (await health.GetAsync(Account, SyncHealthKind.Holdings, Ct)).LastFailureReason.ShouldBe("interrupted");
    }

    [Fact]
    public async Task The_runs_an_earlier_process_left_running_are_failed_at_start_up_and_noted_and_a_run_of_this_process_is_left_alone()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var brokerRuns = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var holdingSyncs = scope.ServiceProvider.GetRequiredService<IHoldingSyncStore>();
        var health = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();
        var processStarted = DateTimeOffset.UtcNow;
        var earlierTrades = BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", processStarted.AddMinutes(-5));
        (await brokerRuns.TryStartAsync(earlierTrades, TimeSpan.FromHours(1), processStarted.AddMinutes(-5), Ct)).ShouldBeTrue();

        (await brokerRuns.FailInterruptedAsync(processStarted, processStarted, Ct)).ShouldBe(1);
        var earlierHoldings = HoldingSync.Start(Account, SyncTrigger.Manual, "o", processStarted.AddMinutes(-4));
        (await holdingSyncs.TryStartAsync(earlierHoldings, TimeSpan.FromHours(1), processStarted.AddMinutes(-4), Ct)).ShouldBeTrue();
        (await holdingSyncs.FailInterruptedAsync(processStarted, processStarted, Ct)).ShouldBe(1);
        var mine = BrokerSyncRun.Start(Account, BrokerSyncKind.Ledger, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", processStarted.AddSeconds(5));
        (await brokerRuns.TryStartAsync(mine, TimeSpan.FromHours(1), processStarted.AddSeconds(5), Ct)).ShouldBeTrue();
        (await brokerRuns.FailInterruptedAsync(processStarted, processStarted.AddSeconds(10), Ct)).ShouldBe(0);

        (await health.GetAsync(Account, SyncHealthKind.Trades, Ct)).LastFailureReason.ShouldBe("interrupted");
        (await health.GetAsync(Account, SyncHealthKind.Holdings, Ct)).LastFailureReason.ShouldBe("interrupted");
        (await health.GetAsync(Account, SyncHealthKind.Ledger, Ct)).IsFailing.ShouldBeFalse();
    }

    // ---- the mapper version, the deepening and the noted windows ----

    [Fact]
    public async Task The_mapper_version_never_goes_down_and_creates_the_row_without_a_watermark()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();
        var runs = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();

        await store.StampMapperVersionAsync(Account, SyncHealthKind.Trades, 3, At(12, 10, 0), Ct);
        await store.StampMapperVersionAsync(Account, SyncHealthKind.Trades, 2, At(12, 10, 1), Ct);

        (await store.GetAsync(Account, SyncHealthKind.Trades, Ct)).MapperVersion.ShouldBe(3);
        (await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct)).ShouldBeNull(); // a row that only holds figures is no watermark
        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), At(12, 10, 2), Ct); // so the first read from a chosen date still starts it
        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 9), At(12, 10, 3), Ct); // and a hole after it is still refused
        (await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct)).ShouldBe(new DateOnly(2026, 9, 30));
        (await store.GetAsync(Account, SyncHealthKind.Trades, Ct)).MapperVersion.ShouldBe(3);
    }

    [Fact]
    public async Task The_earlier_history_state_keeps_the_earliest_date_reached_and_stays_exhausted()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();

        await store.RecordDeepenedAsync(Account, new DateOnly(2024, 1, 1), exhausted: false, new DateOnly(2026, 10, 12), At(12, 20, 0), Ct);
        await store.RecordDeepenedAsync(Account, new DateOnly(2023, 1, 1), exhausted: false, new DateOnly(2026, 10, 13), At(13, 20, 0), Ct);
        await store.RecordDeepenedAsync(Account, null, exhausted: true, new DateOnly(2026, 10, 14), At(14, 20, 0), Ct);
        await store.RecordDeepenedAsync(Account, new DateOnly(2025, 1, 1), exhausted: false, new DateOnly(2026, 10, 15), At(15, 20, 0), Ct); // a later, shallower read changes neither

        var health = await store.GetAsync(Account, SyncHealthKind.Trades, Ct);

        (health.DeepenedTo, health.DeepenExhausted, health.DeepenedOn).ShouldBe((new DateOnly(2023, 1, 1), true, new DateOnly(2026, 10, 15)));
    }

    [Fact]
    public async Task A_window_that_dropped_rows_is_noted_listed_and_cleared_by_a_read_that_covers_it()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncHealthStore>();
        await store.NoteWindowAsync(Account, SyncHealthKind.Trades, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0, At(12, 10, 0), Ct); // clean: no row
        await store.NoteWindowAsync(Account, SyncHealthKind.Trades, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 30), 4, At(12, 10, 0), Ct);
        await store.NoteWindowAsync(Account, SyncHealthKind.Trades, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), 1, At(12, 10, 0), Ct);
        await store.NoteWindowAsync(Account, SyncHealthKind.Ledger, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), 1, At(12, 10, 0), Ct);

        var listed = await store.ListRereadAsync(Account, SyncHealthKind.Trades, Ct);
        await store.NoteWindowAsync(Account, SyncHealthKind.Trades, new DateOnly(2026, 7, 15), new DateOnly(2026, 9, 5), 0, At(13, 10, 0), Ct); // covers August only
        var afterCover = await store.ListRereadAsync(Account, SyncHealthKind.Trades, Ct);
        await store.NoteWindowAsync(Account, SyncHealthKind.Trades, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), 1, At(13, 10, 0), Ct); // still dropping: stays
        var stillDropping = await store.ListRereadAsync(Account, SyncHealthKind.Trades, Ct);

        listed.ShouldBe([new BrokerDateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 30)), new BrokerDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12))]);
        afterCover.ShouldBe([new BrokerDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12))]);
        stillDropping.ShouldBe(afterCover);
        (await store.ListRereadAsync(Account, SyncHealthKind.Ledger, Ct)).Count.ShouldBe(1); // each kind has its own
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.broker_sync_windows WHERE kind = 'trades'", Ct)).ShouldBe(2L); // never deleted
    }

    [Fact]
    public async Task The_dates_of_the_runs_that_dropped_rows_and_the_latest_succeeded_run_of_every_account_are_found_in_one_read()
    {
        await using var admin = await ConnectedAccountAsync();
        const string other = "00000000-0000-7000-8000-00000000a202";
        await AppRoleProbe.ExecuteAsync(admin, AccountSql.Replace(AccountId, other, StringComparison.Ordinal).Replace("probe-heal", "probe-heal-2", StringComparison.Ordinal).Replace("1000000001", "1000000002", StringComparison.Ordinal), Ct);
        using var scope = factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var now = DateTimeOffset.UtcNow;

        async Task<BrokerSyncRun> DoneAsync(Guid account, int minutes, DateOnly from, DateOnly to, BrokerSyncCounts counts, SyncStatus status = SyncStatus.Succeeded)
        {
            var run = BrokerSyncRun.Start(account, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, from, to, "o", now.AddMinutes(minutes));
            (await runs.TryStartAsync(run, TimeSpan.FromHours(5), now.AddMinutes(minutes), Ct)).ShouldBeTrue();
            await runs.CompleteAsync(run.Id, status, counts, null, now.AddMinutes(minutes + 1), Ct);
            return run;
        }

        await DoneAsync(Account, 0, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), BrokerSyncCounts.None with { Dropped = 2 });
        await DoneAsync(Account, 10, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), BrokerSyncCounts.None with { SkippedByReason = new Dictionary<string, int> { ["no_symbol"] = 1 } });
        await DoneAsync(Account, 20, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), BrokerSyncCounts.None with { Dropped = 5 }, SyncStatus.Failed);
        var clean = await DoneAsync(Account, 30, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), BrokerSyncCounts.None);
        var theirs = await DoneAsync(Guid.Parse(other), 5, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), BrokerSyncCounts.None with { Stored = 3 });

        var dropped = await runs.ListDroppedRangesAsync(Account, BrokerSyncKind.Trades, Ct);
        var latest = await runs.ListLastSucceededAsync([Account, Guid.Parse(other), Guid.NewGuid()], BrokerSyncKind.Trades, Ct);

        dropped.ShouldBe([new BrokerDateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), new BrokerDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12))]); // a failed run does not count
        latest.Keys.Order().ShouldBe([Account, Guid.Parse(other)]);
        (latest[Account].Id, latest[Guid.Parse(other)].Id, latest[Guid.Parse(other)].Counts.Stored).ShouldBe((clean.Id, theirs.Id, 3));
        (await runs.ListLastSucceededAsync([], BrokerSyncKind.Trades, Ct)).ShouldBeEmpty();
    }

    // ---- the memory of the attention items ----

    private static AttentionDraft Draft(string id, string severity = "warning", string text = "text", Guid? account = null) =>
        new(id, "sync_failing", severity, "Title", text, """[{"label":"Open","kind":"route","target":"/x"}]""", account);

    /// <summary>What the reconciler does (write) followed by what the notices do (read).</summary>
    private static async Task<IReadOnlyList<StoredAttention>> ReconcileAsync(IAttentionStore store, IReadOnlyList<AttentionDraft> drafts, DateTimeOffset at, TimeSpan keepResolved)
    {
        await store.ReconcileAsync(drafts, at, Ct);
        return await store.ListAsync(at, keepResolved, Ct);
    }

    [Fact]
    public async Task The_memory_keeps_since_resolves_what_no_longer_holds_keeps_it_for_the_time_given_and_starts_a_recurrence_anew()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttentionStore>();
        var keep = TimeSpan.FromHours(24);
        var t0 = At(12, 10, 0);

        var first = await ReconcileAsync(store, [Draft("a", account: Account), Draft("b", "error")], t0, keep);
        var again = await ReconcileAsync(store, [Draft("a", account: Account, text: "changed"), Draft("b", "error")], t0.AddMinutes(5), keep);
        var resolved = await ReconcileAsync(store, [Draft("b", "error")], t0.AddMinutes(10), keep);
        var dayLater = await ReconcileAsync(store, [Draft("b", "error")], t0.AddHours(24).AddMinutes(11), keep);
        var recurred = await ReconcileAsync(store, [Draft("a", account: Account), Draft("b", "error")], t0.AddHours(25), keep);

        first.Select(i => (i.Id, i.Since, i.ResolvedAt)).Order().ShouldBe([("a", t0, (DateTimeOffset?)null), ("b", t0, null)]);
        again.Single(i => i.Id == "a").Text.ShouldBe("changed");
        again.ShouldAllBe(i => i.Since == t0); // since is when it was first seen
        resolved.Single(i => i.Id == "a").ResolvedAt.ShouldBe(t0.AddMinutes(10));
        resolved.Single(i => i.Id == "b").ResolvedAt.ShouldBeNull();
        resolved.Single(i => i.Id == "a").AccountId.ShouldBe(Account);
        dayLater.Select(i => i.Id).ShouldBe(["b"]); // a day and a minute after: dropped
        recurred.Single(i => i.Id == "a").ShouldSatisfyAllConditions(i => i.ResolvedAt.ShouldBeNull(), i => i.Since.ShouldBe(t0.AddHours(25)));
        recurred.Single(i => i.Id == "b").Since.ShouldBe(t0);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.attention_items", Ct)).ShouldBe(2L); // resolving deletes nothing
    }

    [Fact]
    public async Task An_unchanged_item_is_not_written_again()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttentionStore>();
        await ReconcileAsync(store, [Draft("a")], At(12, 10, 0), TimeSpan.FromHours(24));
        var written = await AppRoleProbe.ScalarAsync(admin, "SELECT xmin::text FROM portfolio.attention_items WHERE id = 'a'", Ct);

        await ReconcileAsync(store, [Draft("a")], At(12, 10, 5), TimeSpan.FromHours(24));
        await ReconcileAsync(store, [Draft("a")], At(12, 10, 6), TimeSpan.FromHours(24));

        (await AppRoleProbe.ScalarAsync(admin, "SELECT xmin::text FROM portfolio.attention_items WHERE id = 'a'", Ct)).ShouldBe(written);
    }

    [Fact]
    public async Task With_nothing_to_remember_everything_known_is_resolved()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttentionStore>();
        await ReconcileAsync(store, [Draft("a"), Draft("b")], At(12, 10, 0), TimeSpan.FromHours(24));

        var none = await ReconcileAsync(store, [], At(12, 11, 0), TimeSpan.FromHours(24));

        none.ShouldAllBe(i => i.ResolvedAt == At(12, 11, 0));
        none.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Listing_reads_only_and_shows_the_unresolved_items_and_those_resolved_within_the_time_kept()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttentionStore>();
        var t0 = At(12, 10, 0);
        await store.ReconcileAsync([Draft("a"), Draft("b")], t0, Ct);
        await store.ReconcileAsync([Draft("b")], t0.AddHours(1), Ct); // a is resolved at 11:00
        var written = await AppRoleProbe.ScalarAsync(admin, "SELECT string_agg(xmin::text, ',' ORDER BY id) FROM portfolio.attention_items", Ct);

        var shortly = await store.ListAsync(t0.AddHours(2), TimeSpan.FromHours(24), Ct);
        var later = await store.ListAsync(t0.AddHours(30), TimeSpan.FromHours(24), Ct);

        shortly.Select(i => i.Id).Order().ShouldBe(["a", "b"]);
        later.Select(i => i.Id).ShouldBe(["b"]);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT string_agg(xmin::text, ',' ORDER BY id) FROM portfolio.attention_items", Ct)).ShouldBe(written); // nothing was written
    }

    [Fact]
    public async Task The_retention_deletes_resolved_items_older_than_the_cutoff_in_one_statement_and_never_an_unresolved_one()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttentionStore>();
        var t0 = At(1, 10, 0);
        await store.ReconcileAsync([Draft("old-open"), Draft("old-gone"), Draft("recent-gone")], t0, Ct);
        await store.ReconcileAsync([Draft("old-open"), Draft("recent-gone")], t0.AddMinutes(1), Ct); // old-gone resolved on the 1st
        await store.ReconcileAsync([Draft("old-open")], t0.AddDays(20), Ct); // recent-gone resolved on the 21st

        var deleted = await store.DeleteResolvedBeforeAsync(t0.AddDays(5), Ct); // only what was resolved before the 6th

        deleted.ShouldBe(1);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT string_agg(id, ',' ORDER BY id) FROM portfolio.attention_items", Ct)).ShouldBe("old-open,recent-gone");

        (await store.DeleteResolvedBeforeAsync(t0.AddYears(5), Ct)).ShouldBe(1); // however late the cutoff: only resolved rows go
        (await AppRoleProbe.ScalarAsync(admin, "SELECT string_agg(id, ',' ORDER BY id) FROM portfolio.attention_items", Ct)).ShouldBe("old-open");
    }

    [Fact]
    public async Task The_retention_is_one_set_based_delete_even_for_many_rows()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttentionStore>();
        var t0 = At(1, 10, 0);
        await store.ReconcileAsync([.. Enumerable.Range(0, 200).Select(i => Draft($"item-{i}"))], t0, Ct);
        await store.ReconcileAsync([], t0.AddMinutes(1), Ct);

        var deleted = await store.DeleteResolvedBeforeAsync(t0.AddDays(1), Ct);

        deleted.ShouldBe(200);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.attention_items", Ct)).ShouldBe(0L);
    }

    // ---- the migration ----

    [Fact]
    public async Task A_database_with_watermarks_migrates_keeps_them_and_rolling_back_drops_the_tables_and_the_rows_that_only_hold_figures()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = MigrationDatabase, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"CREATE DATABASE {MigrationDatabase}", Ct);
        }

        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeMigration, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, AccountSql + $"INSERT INTO portfolio.broker_sync_state (account_id, kind, watermark_date, updated_at) VALUES ('{AccountId}', 'trades', '2026-10-05', now());", Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT watermark_date::text FROM portfolio.broker_sync_state WHERE kind = 'trades'", Ct)).ShouldBe("2026-10-05");
            (await AppRoleProbe.ScalarAsync(connection, "SELECT consecutive_failed_days || '/' || attempts || '/' || mapper_version || '/' || deepen_exhausted FROM portfolio.broker_sync_state WHERE kind = 'trades'", Ct)).ShouldBe("0/0/0/false");
            await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.broker_sync_state (account_id, kind, updated_at, last_failure_reason) VALUES ('{AccountId}', 'holdings', now(), 'unavailable'), ('{AccountId}', 'ledger', now(), 'unavailable');", Ct);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.broker_sync_state", Ct)).ShouldBe(1L); // the figures-only rows have no place in the old shape
            (await AppRoleProbe.ScalarAsync(connection, "SELECT watermark_date::text FROM portfolio.broker_sync_state", Ct)).ShouldBe("2026-10-05");
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('broker_sync_windows', 'attention_items')", Ct)).ShouldBe(0L);
            (await ColumnsAsync(connection, "broker_sync_state")).ShouldBe(["account_id", "kind", "watermark_date", "updated_at"]);

            await migrator.MigrateAsync(null, Ct); // and forward again
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('broker_sync_windows', 'attention_items')", Ct)).ShouldBe(2L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }

    [Fact]
    public async Task The_retention_grant_migration_gives_the_app_role_delete_on_the_items_only_and_rolling_back_takes_it_away_again()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = GrantMigrationDatabase, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$", Ct); // the grants are a no-op without the role
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {GrantMigrationDatabase} WITH (FORCE)", Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"CREATE DATABASE {GrantMigrationDatabase}", Ct);
        }

        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            async Task<object?> CanDeleteAsync(string table) => await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', 'DELETE')", Ct);

            await migrator.MigrateAsync(BeforeGrantMigration, Ct);
            (await CanDeleteAsync("attention_items")).ShouldBe(false);

            await migrator.MigrateAsync(null, Ct);
            (await CanDeleteAsync("attention_items")).ShouldBe(true);
            foreach (var table in NeverDeleteTables)
            {
                (await CanDeleteAsync(table)).ShouldBe(false, table); // nothing else gained it
            }

            await migrator.MigrateAsync(BeforeGrantMigration, Ct);
            (await CanDeleteAsync("attention_items")).ShouldBe(false);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'portfolio.attention_items', 'UPDATE')", Ct)).ShouldBe(true); // the rest of the access stays

            await migrator.MigrateAsync(null, Ct); // and forward again
            (await CanDeleteAsync("attention_items")).ShouldBe(true);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {GrantMigrationDatabase} WITH (FORCE)", Ct);
        }
    }
}
