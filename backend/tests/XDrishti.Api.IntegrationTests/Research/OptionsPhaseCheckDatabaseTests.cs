using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionPhaseCheck;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.OptionPhaseCheck;
using XDrishti.Application.OptionReports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Infrastructure.Learning;
using XDrishti.Infrastructure.OptionExpression;
using XDrishti.Infrastructure.OptionPhaseCheck;
using XDrishti.Infrastructure.OptionReports;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// P14-17 criteria 1 and 4 on a real TimescaleDB (the throw-away server of <see cref="ResearchGrantsTests"/>): the phase check runs through the real stores as a role that has SELECT and nothing else on the tables it reads, finds a stored plan item whose cell key has a non-cash
/// eighth part by row id (a six-part and a seven-part key are cash), reads a stored verdict and the run it rests on, and writes nothing. A role without write rights is refused a write; a missing table is NOT EVALUABLE, not an error.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionsPhaseCheckDatabaseTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string Role = "xd_options_phase_reader";

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 6, 30, 0, TimeSpan.Zero);

    private static readonly string RunId = new('1', 64);

    private static readonly string CashKey = new('2', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Inputs(long watermark) =>
        "{\"format\":\"expr-remark/1\",\"archive_watermark\":" + watermark + ",\"cost_sheet\":\"gross\",\"spread\":[]}";

    private async Task<string> ReaderConnectionAsync()
    {
        await StoreKit.ExecuteAsync(
            server.Owner,
            $"""
            DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{Role}') THEN CREATE ROLE {Role} LOGIN PASSWORD 'ro-test-only'; END IF; END $$;
            GRANT CONNECT ON DATABASE xdrishti TO {Role};
            GRANT USAGE ON SCHEMA research, trading, paper, options TO {Role};
            REVOKE ALL ON ALL TABLES IN SCHEMA research, trading, paper, options FROM {Role};
            GRANT SELECT ON research.extension_evaluations, research.expression_runs, research.expression_trades, trading.plan_items, paper.entries, options.fetch_log, options.chain_recorder_switch, options.backfill_switch TO {Role};
            """,
            Ct);
        return new NpgsqlConnectionStringBuilder(server.Owner) { Username = Role, Password = "ro-test-only" }.ConnectionString;
    }

    private static ExtensionEvaluationRecord Drop(string family) =>
        new(new string('c', 64), family, "O", null, "expr-1", "none", new string('b', 64), null, new string('d', 64), 3, 1.5, "quarter", 6, 6, 500, null, "{}", "DROP", [], [], "{}",
            "{\"sources\":{\"expression_run\":\"" + RunId + "\",\"cash_run\":\"" + CashKey + "\",\"profile\":\"" + ExpressionProfiles.PrimaryHashOf(family) + "\",\"primary_profile\":\"true\",\"charge_basis\":\"net\"}}", false);

    private static GetOptionsPhaseCheckHandler Handler(NpgsqlDataSource readOnly) =>
        new(new ExtensionEvaluationStore(readOnly), new OptionsPhaseReader(readOnly), new OptionReportReader(readOnly), new NoArchive(), new NoBars(), new Engine(), new Sheets(), new CashRuns(), new Models(), new Remark(), new Stale(), new FakeTimeProvider(Now), NullLogger<GetOptionsPhaseCheckHandler>.Instance);

    private sealed class NoArchive : IQueryHandler<GetOptionArchiveSummaryQuery, Result<OptionArchiveSummaryView>>
    {
        public ValueTask<Result<OptionArchiveSummaryView>> HandleAsync(GetOptionArchiveSummaryQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success(new OptionArchiveSummaryView([])));
    }

    private sealed class Engine : ISimulationEngineVersion
    {
        public string EngineVersion => "sim-9";
    }

    private sealed class Sheets : ICostSheetHashes
    {
        public ValueTask<string?> RatesHashAsync(string sheetId, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
    }

    private sealed class CashRuns : ICashRunHeaders
    {
        public ValueTask<PhaseCashRun?> FindAsync(string runKey, CancellationToken cancellationToken) => ValueTask.FromResult<PhaseCashRun?>(new PhaseCashRun(runKey, "sim-9", null));
    }

    private sealed class Models : ISpreadModelIds
    {
        public ValueTask<string?> LatestAsync(Guid underlyingId, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
    }

    private sealed class Remark : IRemarkSettings
    {
        public int MaxIvAgeMinutes => 60;

        public string? CondorCostSheetId => null;
    }

    private sealed class Stale : IOptionsPhaseCheckSettings
    {
        public int StaleAfterDays => 90;
    }

    private sealed class NoBars : IBarQueries
    {
        public ValueTask<BarTotals> GetBarTotalsAsync(DateTimeOffset expectedStart, CancellationToken cancellationToken) => ValueTask.FromResult(new BarTotals(1, 10, Now.AddDays(-767), Now, 0, 0));

        public ValueTask<AggregateTotals> GetAggregateTotalsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<DataInstrumentPage> ListInstrumentsAsync(DataInstrumentSearch search, DateTimeOffset expectedStart, DateOnly expectedSession, TradingCalendar calendar, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<BarsInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<BarRow>> ReadBarsAsync(Guid instrumentId, Timeframe timeframe, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<BarRow>> ReadDailyBarsAsync(Guid instrumentId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task SeedPlanItemAsync(string planKey, int day, string cellKey)
    {
        await StoreKit.ExecuteAsync(
            server.Owner,
            $$"""
            INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('{{planKey}}', '2026-10-14', '2026-10-14 03:35:00+00', {{day}}, repeat('b', 64), 'b', true, 'n', 'l', 'probe', now());
            INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable)
            SELECT id, 1, 'primary', 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', '{}', '{{cellKey}}', 1, 'd', 'b', '{}' FROM trading.plans WHERE plan_key = '{{planKey}}';
            """,
            Ct);
    }

    [Fact]
    public async Task The_phase_check_runs_as_a_select_only_role_finds_the_non_cash_plan_item_by_row_id_reads_the_verdict_and_writes_nothing()
    {
        // the switch tables are emptied too: the test that reads the newest switch rows leaves its own behind, and the order of the tests of a class is not fixed (CF-11 gate run: the first test saw its switch row)
        await StoreKit.ExecuteAsync(server.Owner, "TRUNCATE research.extension_evaluations; DELETE FROM trading.plan_items; DELETE FROM trading.plans; DELETE FROM options.chain_recorder_switch; DELETE FROM options.backfill_switch", Ct);
        await SeedPlanItemAsync("p14-clean", 1, "strat|5m|BUY|style|exit|*"); // six parts: cash
        await SeedPlanItemAsync("p14-seven", 2, "strat|5m|BUY|style|exit|*|*"); // seven parts: a universe, still cash
        await SeedPlanItemAsync("p14-cash", 4, "strat|5m|BUY|style|exit|*|*|cash"); // an eighth part that says cash is not a non-cash expression
        await SeedPlanItemAsync("p14-bad", 3, "strat|5m|BUY|style|exit|*|*|option_buy"); // eight parts: a non-cash expression
        var badId = await StoreKit.CountAsync(server.Owner, "SELECT id FROM trading.plan_items WHERE cell_key LIKE '%option_buy'", Ct);
        await using var app = NpgsqlDataSource.Create(server.App);
        await new ExtensionEvaluationStore(app).AppendAsync(Drop(ExpressionFamilies.OptionBuy), Now, Ct);
        await new ExpressionTradeStore(app).AppendAsync(new ExpressionRunRow(RunId, CashKey, ExpressionProfiles.PrimaryHashOf(ExpressionFamilies.OptionBuy)!, "expr-1", Inputs(0), 0, 0, "{}", null, []), [], Ct);
        var readerString = await ReaderConnectionAsync();
        await using var readOnly = NpgsqlDataSource.Create(readerString);
        var before = (await CountAsync("research.extension_evaluations"), await CountAsync("research.expression_runs"), await CountAsync("trading.plan_items"));

        var result = await Handler(readOnly).HandleAsync(new OptionsPhaseCheckQuery([]), Ct);

        result.IsSuccess.ShouldBeTrue();
        var report = result.Value.Report;
        report.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        var violation = report.Violations.ShouldHaveSingleItem();
        (violation.Source, violation.Id).ShouldBe(("trading.plan_items", badId));
        var line = report.Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);
        (line.Status, line.Resolved, line.Basis).ShouldBe(("DROP", true, "net"));
        report.Families.Where(f => f.Family != ExpressionFamilies.OptionBuy).ShouldAllBe(f => f.Status == "none");
        report.YearsHeld.ShouldNotBeNull().ShouldBe(2.1, 0.01);
        (report.RecorderOn, report.BackfillOn).ShouldBe((false, false));
        (await CountAsync("research.extension_evaluations"), await CountAsync("research.expression_runs"), await CountAsync("trading.plan_items")).ShouldBe(before);
        await ShouldBeRefusedAsync(readerString, "INSERT INTO options.chain_recorder_switch (enabled, actor, reason) VALUES (true, 'owner:x@example.invalid', 'r')");
        await ShouldBeRefusedAsync(readerString, "DELETE FROM research.extension_evaluations");
        await ShouldBeRefusedAsync(readerString, "UPDATE trading.plan_items SET cell_key = 'x'");
    }

    [Fact]
    public async Task The_reader_finds_the_run_inputs_the_fetch_end_time_and_the_newest_switch_rows_and_the_same_rows_give_the_same_text()
    {
        await StoreKit.ExecuteAsync(server.Owner, "TRUNCATE research.extension_evaluations; DELETE FROM trading.plan_items; DELETE FROM trading.plans", Ct);
        await using var app = NpgsqlDataSource.Create(server.App);
        await new ExtensionEvaluationStore(app).AppendAsync(Drop(ExpressionFamilies.OptionBuy), Now, Ct);
        await new ExpressionTradeStore(app).AppendAsync(new ExpressionRunRow(RunId, CashKey, ExpressionProfiles.PrimaryHashOf(ExpressionFamilies.OptionBuy)!, "expr-1", Inputs(0), 0, 0, "{}", null, []), [], Ct);
        await StoreKit.ExecuteAsync(
            server.Owner,
            "INSERT INTO options.chain_recorder_switch (enabled, actor, reason) VALUES (true, 'owner:hem@example.invalid', 'on'); INSERT INTO options.backfill_switch (enabled, actor, reason) VALUES (false, 'owner:hem@example.invalid', 'off')",
            Ct);
        var readerString = await ReaderConnectionAsync();
        await using var readOnly = NpgsqlDataSource.Create(readerString);
        var reader = new OptionsPhaseReader(readOnly);

        var runs = await reader.RunInputsAsync([RunId, new string('9', 64)], Ct);
        var ended = await reader.FetchEndedAtAsync([long.MaxValue], Ct);
        var first = await Handler(readOnly).HandleAsync(new OptionsPhaseCheckQuery([]), Ct);
        var second = await Handler(readOnly).HandleAsync(new OptionsPhaseCheckQuery([]), Ct);

        var run = runs.ShouldHaveSingleItem();
        (run.RunId, run.RemarkVersion).ShouldBe((RunId, "expr-1"));
        run.InputsJson.ShouldContain("archive_watermark");
        ended.ShouldBeEmpty();
        (await reader.IsChainRecorderOnAsync(Ct), await reader.IsBackfillOnAsync(Ct)).ShouldBe((true, false));
        (await reader.NonCashCellRowsAsync(5, Ct)).ShouldBeEmpty();
        first.Value.Text.ShouldBe(second.Value.Text);
        first.Value.Json.ShouldBe(second.Value.Json);
    }

    [Fact]
    public async Task A_missing_table_is_not_evaluable_not_an_error()
    {
        // no plan item is left either: a stored non-cash plan item of another test is a violation, which reads NOT MET before the missing table can read NOT EVALUABLE (the order of the tests of a class is not fixed)
        await StoreKit.ExecuteAsync(server.Owner, "TRUNCATE research.extension_evaluations; DELETE FROM trading.plan_items; DELETE FROM trading.plans", Ct);
        var readerString = await ReaderConnectionAsync();
        await StoreKit.ExecuteAsync(server.Owner, "ALTER TABLE research.extension_evaluations RENAME TO extension_evaluations_hidden", Ct);
        try
        {
            await using var readOnly = NpgsqlDataSource.Create(readerString);
            var missing = await Handler(readOnly).HandleAsync(new OptionsPhaseCheckQuery([]), Ct);

            missing.IsSuccess.ShouldBeTrue();
            missing.Value.Report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
            missing.Value.Report.Families.ShouldAllBe(f => f.Status == "unreadable");
        }
        finally
        {
            await StoreKit.ExecuteAsync(server.Owner, "ALTER TABLE research.extension_evaluations_hidden RENAME TO extension_evaluations", Ct);
        }
    }

    private static async Task ShouldBeRefusedAsync(string connectionString, string sql)
    {
        var failure = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(connectionString, sql, Ct));

        failure.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }
}
