using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// The tables of migration AddDhanTradeAndLedgerSync (P2-13, ADR 0025): their columns and checks, the grants (the runtime role never deletes runs, watermarks, ledger entries or trades), a window's single unit of work over the stores,
/// the exclusion between holdings syncs and broker history runs, the watermark that only moves forward, and a database that already holds trades when the migration runs.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerHistoryDatabaseTests(ApiFactory factory)
{
    private const string MigrationDatabase = "xd_broker_history_migration_probe";

    private const string BeforeMigration = "20261013211905_AddPreviousCloseTime";

    private const string AccountId = "00000000-0000-7000-8000-00000000a101";

    private const string Run = "00000000-0000-7000-8000-00000000b101";

    private static readonly Guid Account = Guid.Parse(AccountId);

    private static readonly string[] Tables = ["broker_sync_runs", "broker_sync_state", "ledger_entries", "transactions"];

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AccountSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{AccountId}', 'probe-bh', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    private static string RunSql(string kind = "'trades'", string trigger = "'manual'", string status = "'running'", string id = Run, string counts = "'{}'") =>
        $"""
        INSERT INTO portfolio.broker_sync_runs (id, account_id, kind, trigger, from_date, to_date, status, counts, started_by, started_at)
        VALUES ('{id}', '{AccountId}', {kind}, {trigger}, '2026-10-05', '2026-10-12', {status}, {counts}, 'o', now())
        """;

    private static string EntrySql(string entryClass = "'other'", string external = "abc") =>
        $"""
        INSERT INTO portfolio.ledger_entries (id, account_id, entry_date, voucher_no, description, narration, amount, class, external_id, run_id, created_at)
        VALUES (gen_random_uuid(), '{AccountId}', '2026-10-05', 'V1', 'RECEIPT', 'x', 10, {entryClass}, '{external}', '{Run}', now())
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

    [Fact]
    public async Task The_tables_have_the_columns_of_the_contract_and_the_unique_indexes()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        (await ColumnsAsync(connection, "broker_sync_runs")).ShouldBe(["id", "account_id", "kind", "trigger", "from_date", "to_date", "status", "counts", "started_by", "started_at", "finished_at", "error"]);
        (await ColumnsAsync(connection, "broker_sync_state")).ShouldBe(
        [
            "account_id", "kind", "watermark_date", "updated_at",
            "attempts", "consecutive_failed_days", "deepen_exhausted", "deepened_on", "deepened_to", "last_failed_at", "last_failure_reason", "mapper_version", // ADR 0027
        ]);
        (await ColumnsAsync(connection, "ledger_entries")).ShouldBe(["id", "account_id", "entry_date", "voucher_no", "description", "narration", "exchange", "amount", "class", "running_balance", "external_id", "run_id", "created_at", "batch_id", "isin", "quantity", "source", "voided_at", "voided_by"]);
        (await AppRoleProbe.ScalarAsync(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'broker_sync_runs' AND column_name = 'counts'", Ct)).ShouldBe("jsonb");
        (await AppRoleProbe.ScalarAsync(connection, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'portfolio' AND indexname = 'ux_broker_sync_runs_one_running'", Ct))!.ToString()!.ShouldContain("WHERE ((status)::text = 'running'::text)");
        (await AppRoleProbe.ScalarAsync(connection, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'portfolio' AND indexname = 'ux_ledger_entries_account_external'", Ct))!.ToString()!.ShouldContain("UNIQUE");
    }

    public static TheoryData<string, string> BadRows => new()
    {
        { RunSql(kind: "'cash'"), "ck_broker_sync_runs_kind" },
        { RunSql(trigger: "'auto'"), "ck_broker_sync_runs_trigger" },
        { RunSql(status: "'done'"), "ck_broker_sync_runs_status" },
        { "INSERT INTO portfolio.broker_sync_state (account_id, kind, watermark_date, updated_at) VALUES ('" + AccountId + "', 'cash', '2026-10-05', now())", "ck_broker_sync_state_kind" },
    };

    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task The_database_refuses_a_row_the_rules_never_produce(string sql, string constraint)
    {
        await using var connection = await ConnectedAccountAsync();

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, sql, Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, constraint));
    }

    [Fact]
    public async Task A_ledger_entry_has_one_of_the_five_classes_and_a_unique_identity_per_account()
    {
        await using var connection = await ConnectedAccountAsync();
        await AppRoleProbe.ExecuteAsync(connection, RunSql(status: "'succeeded'"), Ct);

        var badClass = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, EntrySql(entryClass: "'gifts'"), Ct));
        await AppRoleProbe.ExecuteAsync(connection, EntrySql(), Ct);
        var twin = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, EntrySql(), Ct));

        (badClass.ConstraintName, twin.ConstraintName).ShouldBe(("ck_ledger_entries_class", "ux_ledger_entries_account_external"));
    }

    [Fact]
    public async Task A_trade_may_come_from_the_broker_with_its_run_and_other_sources_are_still_refused()
    {
        await using var connection = await ConnectedAccountAsync();
        await AppRoleProbe.ExecuteAsync(connection, RunSql(status: "'succeeded'"), Ct);
        string Trade(string source, string run) =>
            $"INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at, run_id) VALUES (gen_random_uuid(), '{AccountId}', 'EXMPL', 'NSE', 'buy', '2026-10-05', 1, 1, 0, '{source}', 1, 't:{source}', 'o', now(), {run})";

        await AppRoleProbe.ExecuteAsync(connection, Trade("broker", $"'{Run}'"), Ct);
        var other = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, Trade("api", "NULL"), Ct));
        var unknownRun = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, Trade("csv", "gen_random_uuid()"), Ct));

        (other.ConstraintName, unknownRun.ConstraintName).ShouldBe(("ck_transactions_source", "fk_transactions_broker_sync_runs_run_id"));
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_runs_watermarks_entries_and_trades_but_never_delete_them()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, AccountSql + RunSql(status: "'succeeded'") + ";\n" + EntrySql() + ";", Ct);
                await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.broker_sync_state (account_id, kind, watermark_date, updated_at) VALUES ('{AccountId}', 'trades', '2026-10-05', now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.broker_sync_state SET watermark_date = '2026-10-06'", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.broker_sync_runs SET status = 'failed', error = 'x'", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.ledger_entries SET narration = 'y'", Ct);

                foreach (var table in Tables)
                {
                    var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, $"DELETE FROM portfolio.{table}", Ct));
                    refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, table);
                    foreach (var privilege in Privileges)
                    {
                        (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', '{privilege}')", Ct)).ShouldBe(true, $"{table} {privilege}");
                    }
                }
            },
            Ct);
    }

    // ---- the stores in one unit of work ----

    private static Transaction Trade(Guid run) =>
        Transaction.Create(Account, null, null, "EXMPL", "NSE", TradeSide.Buy, new DateOnly(2026, 10, 5), new TimeOnly(10, 0), 10, 90m, 0m, TradeSource.Broker, null, 1, "t:20261005:T1", "o", DateTimeOffset.UtcNow, run);

    private static LedgerEntry Entry(Guid run) =>
        LedgerEntry.Create(Account, new DateOnly(2026, 10, 5), "V1", "RECEIPT", "FUNDS DEPOSIT", "NSE-CAPITAL", 100m, LedgerClass.FundsIn, 100m, "id-1", run, DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_window_is_one_unit_of_work_the_trades_the_entries_the_progress_and_the_watermark_are_saved_together_or_not_at_all()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var runs = services.GetRequiredService<IBrokerSyncStore>();
        var trades = services.GetRequiredService<ITradeStore>();
        var ledger = services.GetRequiredService<ILedgerStore>();
        var unitOfWork = services.GetRequiredService<IPortfolioUnitOfWork>();
        var now = DateTimeOffset.UtcNow;
        var run = BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now);
        (await runs.TryStartAsync(run, TimeSpan.FromMinutes(30), now, Ct)).ShouldBeTrue();
        var progress = BrokerSyncCounts.None with { WindowsDone = 1, WindowsTotal = 2, Stored = 1 };

        async ValueTask<Result> Work(bool succeed, CancellationToken token)
        {
            trades.Add([Trade(run.Id)]);
            ledger.Add([Entry(run.Id)]);
            await unitOfWork.SaveAsync(token);
            await runs.UpdateProgressAsync(run.Id, progress, token);
            await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), now, token);
            return succeed ? Result.Success() : Result.Failure(PortfolioErrors.HistoryChanged);
        }

        var refused = await unitOfWork.RunAsync(token => Work(false, token), Ct);
        var crashed = await Should.ThrowAsync<InvalidOperationException>(async () => await unitOfWork.RunAsync(async token =>
        {
            await Work(true, token);
            throw new InvalidOperationException("the database went away");
        }, Ct));

        refused.IsFailure.ShouldBeTrue();
        crashed.Message.ShouldContain("went away");
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.transactions", Ct)).ShouldBe(0L);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.ledger_entries", Ct)).ShouldBe(0L);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.broker_sync_state", Ct)).ShouldBe(0L);
        (await runs.GetAsync(run.Id, Ct))!.Counts.WindowsDone.ShouldBe(0);
        (await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct)).ShouldBeNull();

        var saved = await unitOfWork.RunAsync(token => Work(true, token), Ct);

        saved.IsSuccess.ShouldBeTrue();
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.transactions WHERE source = 'broker'", Ct)).ShouldBe(1L);
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.ledger_entries", Ct)).ShouldBe(1L);
        (await runs.GetAsync(run.Id, Ct))!.Counts.ShouldSatisfyAllConditions(c => c.WindowsDone.ShouldBe(1), c => c.WindowsTotal.ShouldBe(2), c => c.Stored.ShouldBe(1));
        (await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct)).ShouldBe(new DateOnly(2026, 10, 9));
    }

    [Fact]
    public async Task The_watermark_only_moves_forward_and_each_kind_has_its_own()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var now = DateTimeOffset.UtcNow;

        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), now, Ct);
        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), now, Ct);
        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Ledger, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), now, Ct);

        (await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct)).ShouldBe(new DateOnly(2026, 10, 9));
        (await runs.GetWatermarkAsync(Account, BrokerSyncKind.Ledger, Ct)).ShouldBe(new DateOnly(2026, 10, 2));
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.broker_sync_state", Ct)).ShouldBe(2L);
    }

    [Fact]
    public async Task The_watermark_never_jumps_over_a_hole_a_window_must_start_at_or_before_the_day_after_it()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var now = DateTimeOffset.UtcNow;
        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), now, Ct); // the first read from a chosen date has no watermark to follow

        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 12), now, Ct); // starts two days after it: 1 October would never be read
        var jumped = await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct);
        await runs.MoveWatermarkAsync(Account, BrokerSyncKind.Trades, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), now, Ct); // starts on the day after it
        var moved = await runs.GetWatermarkAsync(Account, BrokerSyncKind.Trades, Ct);

        (jumped, moved).ShouldBe((new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 12)));
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.broker_sync_state", Ct)).ShouldBe(1L);
    }

    [Fact]
    public async Task The_latest_successful_run_and_the_run_before_are_found_without_skipped_or_failed_ones_getting_in_the_way()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var now = DateTimeOffset.UtcNow;
        BrokerSyncRun Started(int minutes) => BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now.AddMinutes(minutes));
        var good = Started(0);
        var failed = Started(10);
        var current = Started(20);
        (await runs.TryStartAsync(good, TimeSpan.FromHours(1), good.StartedAt, Ct)).ShouldBeTrue();
        await runs.CompleteAsync(good.Id, SyncStatus.Succeeded, BrokerSyncCounts.None with { Rebuilt = 2 }, null, now.AddMinutes(1), Ct);
        (await runs.TryStartAsync(failed, TimeSpan.FromHours(1), failed.StartedAt, Ct)).ShouldBeTrue();
        await runs.CompleteAsync(failed.Id, SyncStatus.Failed, BrokerSyncCounts.None, "x", now.AddMinutes(11), Ct);
        await runs.RecordSkippedAsync(BrokerSyncRun.Skipped(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, new DateOnly(2026, 10, 12), "s", "token_expired", now.AddMinutes(15)), Ct);
        (await runs.TryStartAsync(current, TimeSpan.FromHours(1), current.StartedAt, Ct)).ShouldBeTrue();

        var lastGood = await runs.GetLastSucceededAsync(Account, BrokerSyncKind.Trades, Ct);
        var before = await runs.GetPreviousFinishedAsync(Account, BrokerSyncKind.Trades, current.Id, Ct);
        var none = await runs.GetLastSucceededAsync(Account, BrokerSyncKind.Ledger, Ct);

        (lastGood!.Id, lastGood.Counts.Rebuilt).ShouldBe((good.Id, 2));
        before!.Id.ShouldBe(failed.Id); // the skipped run, later than the failed one, never read anything
        none.ShouldBeNull();
    }

    [Fact]
    public async Task A_holdings_sync_and_a_broker_history_run_of_one_account_never_run_together()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var brokerRuns = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var holdingSyncs = scope.ServiceProvider.GetRequiredService<IHoldingSyncStore>();
        var now = DateTimeOffset.UtcNow;
        HoldingSync Sync() => HoldingSync.Start(Account, SyncTrigger.Manual, "o", now);
        BrokerSyncRun BrokerRun(BrokerSyncKind kind) => BrokerSyncRun.Start(Account, kind, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now);

        var first = BrokerRun(BrokerSyncKind.Trades);
        (await brokerRuns.TryStartAsync(first, TimeSpan.FromMinutes(30), now, Ct)).ShouldBeTrue();
        var holdingsWhileBroker = await holdingSyncs.TryStartAsync(Sync(), TimeSpan.FromMinutes(10), now, Ct);
        var ledgerWhileTrades = await brokerRuns.TryStartAsync(BrokerRun(BrokerSyncKind.Ledger), TimeSpan.FromMinutes(30), now, Ct);
        await brokerRuns.CompleteAsync(first.Id, SyncStatus.Succeeded, BrokerSyncCounts.None, null, now, Ct);
        var holding = Sync();
        var holdingsAfter = await holdingSyncs.TryStartAsync(holding, TimeSpan.FromMinutes(10), now, Ct);
        var brokerWhileHoldings = await brokerRuns.TryStartAsync(BrokerRun(BrokerSyncKind.Ledger), TimeSpan.FromMinutes(30), now, Ct);
        await holdingSyncs.CompleteAsync(holding.Id, SyncStatus.Succeeded, SyncCounts.None, null, now, Ct);
        var brokerAfter = await brokerRuns.TryStartAsync(BrokerRun(BrokerSyncKind.Ledger), TimeSpan.FromMinutes(30), now, Ct);

        (holdingsWhileBroker, ledgerWhileTrades, holdingsAfter, brokerWhileHoldings, brokerAfter).ShouldBe((false, false, true, false, true));
        (await brokerRuns.IsRunningAsync(Account, Ct)).ShouldBeTrue();
        (await AppRoleProbe.ScalarAsync(admin, "SELECT count(*) FROM portfolio.broker_sync_runs", Ct)).ShouldBe(2L);
    }

    [Fact]
    public async Task A_broker_run_that_ran_past_its_limit_is_failed_by_the_next_start_and_no_longer_blocks_a_holdings_sync()
    {
        await using var admin = await ConnectedAccountAsync();
        using var scope = factory.Services.CreateScope();
        var brokerRuns = scope.ServiceProvider.GetRequiredService<IBrokerSyncStore>();
        var holdingSyncs = scope.ServiceProvider.GetRequiredService<IHoldingSyncStore>();
        var now = DateTimeOffset.UtcNow;
        var old = BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now.AddHours(-2));
        (await brokerRuns.TryStartAsync(old, TimeSpan.FromMinutes(30), now.AddHours(-2), Ct)).ShouldBeTrue();

        var holdings = await holdingSyncs.TryStartAsync(HoldingSync.Start(Account, SyncTrigger.Manual, "o", now), TimeSpan.FromMinutes(10), now, Ct);
        await holdingSyncs.CompleteAsync((await holdingSyncs.GetLastAsync(Account, Ct))!.Id, SyncStatus.Succeeded, SyncCounts.None, null, now, Ct);
        var next = await brokerRuns.TryStartAsync(BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), "o", now), TimeSpan.FromMinutes(30), now, Ct);

        (holdings, next).ShouldBe((true, true));
        (await AppRoleProbe.ScalarAsync(admin, $"SELECT status FROM portfolio.broker_sync_runs WHERE id = '{old.Id}'", Ct)).ShouldBe("failed");
    }

    [Fact]
    public async Task A_holding_that_changed_since_it_was_read_makes_a_run_once_fail_without_saving_or_repeating_the_work()
    {
        await using var admin = await ConnectedAccountAsync();
        const string Bucket = "00000000-0000-7000-8000-00000000c101";
        const string Holding = "00000000-0000-7000-8000-00000000d101";
        await AppRoleProbe.ExecuteAsync(admin, $"""
            INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());
            INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
            VALUES ('{Holding}', '{AccountId}', '{Bucket}', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now());
            """, Ct);
        using var scope = factory.Services.CreateScope();
        var holdings = scope.ServiceProvider.GetRequiredService<IHoldingStore>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPortfolioUnitOfWork>();
        var runs = 0;

        var result = await unitOfWork.RunOnceAsync(
            async token =>
            {
                runs++;
                var held = (await holdings.ListForAccountAsync(Account, token)).Single();
                holdings.Touch(held);
                await AppRoleProbe.ExecuteAsync(admin, $"UPDATE portfolio.holdings SET qty = 11 WHERE id = '{Holding}'", token); // a quote or a sync saves it meanwhile
                await unitOfWork.SaveAsync(token);
                return Result.Success();
            },
            Ct);

        result.Error.ShouldBe(PortfolioErrors.HoldingChanged);
        runs.ShouldBe(1);
        (await AppRoleProbe.ScalarAsync(admin, $"SELECT qty FROM portfolio.holdings WHERE id = '{Holding}'", Ct)).ShouldBe(11L);
    }

    [Fact]
    public async Task A_run_whose_holding_changes_again_after_the_repeat_ends_as_a_holding_changed_result_and_not_an_exception()
    {
        await using var admin = await ConnectedAccountAsync();
        const string Bucket = "00000000-0000-7000-8000-00000000c101";
        const string Holding = "00000000-0000-7000-8000-00000000d101";
        await AppRoleProbe.ExecuteAsync(admin, $"""
            INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());
            INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
            VALUES ('{Holding}', '{AccountId}', '{Bucket}', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now());
            """, Ct);
        using var scope = factory.Services.CreateScope();
        var holdings = scope.ServiceProvider.GetRequiredService<IHoldingStore>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPortfolioUnitOfWork>();
        var runs = 0;

        var result = await unitOfWork.RunAsync(
            async token =>
            {
                runs++;
                var held = (await holdings.ListForAccountAsync(Account, token)).Single();
                holdings.Touch(held);
                await AppRoleProbe.ExecuteAsync(admin, $"UPDATE portfolio.holdings SET qty = qty + 1 WHERE id = '{Holding}'", token); // a quote saves it meanwhile, every time
                await unitOfWork.SaveAsync(token);
                return Result.Success();
            },
            Ct);

        result.Error.ShouldBe(PortfolioErrors.HoldingChanged);
        runs.ShouldBe(2);
    }

    // ---- the migration ----

    [Fact]
    public async Task A_database_that_already_holds_trades_migrates_keeps_them_and_rolling_back_turns_broker_trades_into_file_trades()
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
            await AppRoleProbe.ExecuteAsync(connection, AccountSql + $"""
                INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at)
                VALUES (gen_random_uuid(), '{AccountId}', 'EXMPL', 'NSE', 'buy', '2026-10-05', 1, 1, 0, 'csv', 1, 't:20261005:T1', 'o', now());
                """, Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.transactions WHERE source = 'csv' AND run_id IS NULL", Ct)).ShouldBe(1L);
            await AppRoleProbe.ExecuteAsync(connection, RunSql(status: "'succeeded'") + ";\n" + $"""
                INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at, run_id)
                VALUES (gen_random_uuid(), '{AccountId}', 'EXMPL', 'NSE', 'buy', '2026-10-06', 1, 1, 0, 'broker', 1, 't:20261006:T2', 'o', now(), '{Run}');
                """, Ct);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.transactions WHERE source = 'csv'", Ct)).ShouldBe(2L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('broker_sync_runs', 'broker_sync_state', 'ledger_entries')", Ct)).ShouldBe(0L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }
}
