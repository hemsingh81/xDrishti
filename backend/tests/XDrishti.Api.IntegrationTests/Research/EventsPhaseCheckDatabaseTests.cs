using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.LearningPhase;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Infrastructure.FilingArchive;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// P15-12 criterion 3 and the "missing table is NOT EVALUABLE" rule on a real TimescaleDB (the throw-away server of <see cref="ResearchGrantsTests"/>): the phase check runs through the real stores as a role that has SELECT and nothing else on the two tables it reads, writes nothing, answers NOT MET for the
/// event filter ON against a DROP with the decision row and who made it, and answers NOT EVALUABLE, not an error, when a table is missing.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class EventsPhaseCheckDatabaseTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string Role = "xd_events_reader";

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 6, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExtensionEvaluationRecord Filter(string id, string verdict, int days = 2) =>
        new(id, ExtensionFamilies.Filter, "F", null, $"evf-1:N={days}", "none", new string('b', 64), null, new string('d', 64), 3, 1.5, "quarter", 0, 0, 400, null, "{\"share_lost_to_no_upcoming_entry\":0.25}", verdict, [], [], "{}",
            "{\"sources\":{\"rule_version\":\"evf-1\",\"lag_minutes\":\"0\"}}", false);

    private async Task<string> ReaderConnectionAsync()
    {
        await StoreKit.ExecuteAsync(
            server.Owner,
            $"""
            DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{Role}') THEN CREATE ROLE {Role} LOGIN PASSWORD 'ro-test-only'; END IF; END $$;
            GRANT CONNECT ON DATABASE xdrishti TO {Role};
            GRANT USAGE ON SCHEMA research, filings TO {Role};
            REVOKE ALL ON ALL TABLES IN SCHEMA research, filings FROM {Role};
            GRANT SELECT ON research.extension_evaluations, filings.event_filter_decisions TO {Role};
            """,
            Ct);
        return new NpgsqlConnectionStringBuilder(server.Owner) { Username = Role, Password = "ro-test-only" }.ConnectionString;
    }

    private static GetEventsPhaseCheckHandler Handler(NpgsqlDataSource readOnly) =>
        new(new ExtensionEvaluationStore(readOnly), new EventFilterDecisions(readOnly), new NoFilings(), new NoInstruments(), new NoCalendar(), new Lag(), new NoBars(), new Engine(), new FakeTimeProvider(Now), NullLogger<GetEventsPhaseCheckHandler>.Instance);

    private sealed class Lag : IFilingSettings
    {
        public int LagMinutes => 0;

        public long MaxFilesBytes => 0;

        public long MinFreeBytes => 0;
    }

    private sealed class Engine : ISimulationEngineVersion
    {
        public string EngineVersion => "sim-9";
    }

    private sealed class NoFilings : IFilingReader
    {
        public ValueTask<FilingCoverageData> CoverageAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) => ValueTask.FromResult(new FilingCoverageData([], 0, new Dictionary<string, int>()));

        public ValueTask<IReadOnlyList<FilingView>> ListAsync(Guid? instrumentId, FilingKind? kind, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<FilingView>> ByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<FilingView?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoInstruments : IFilingInstruments
    {
        public ValueTask<InstrumentMatch> ResolveAsync(string symbol, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TrackedInstrument>> TrackedAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<TrackedInstrument>>([]);
    }

    private sealed class NoCalendar : IResultsCalendar
    {
        public ValueTask<ResultsAnswer> DaysToResults(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<Guid, ResultsAnswer>> DaysToResults(IReadOnlyCollection<Guid> instrumentIds, DateTimeOffset t, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<UpcomingMeeting>> UpcomingAsync(DateTimeOffset t, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoBars : IBarQueries
    {
        public ValueTask<BarTotals> GetBarTotalsAsync(DateTimeOffset expectedStart, CancellationToken cancellationToken) => ValueTask.FromResult(new BarTotals(0, 0, null, null, 0, 0));

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

    [Fact]
    public async Task The_phase_check_runs_as_a_select_only_role_reports_the_filter_on_against_a_drop_and_writes_nothing()
    {
        await StoreKit.ExecuteAsync(server.Owner, "TRUNCATE research.extension_evaluations; TRUNCATE filings.event_filter_decisions RESTART IDENTITY", Ct);
        await using var app = NpgsqlDataSource.Create(server.App);
        await new ExtensionEvaluationStore(app).AppendAsync(Filter(new string('1', 64), "DROP"), Now, Ct);
        await StoreKit.ExecuteAsync(
            server.App,
            "INSERT INTO filings.event_filter_decisions (enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot) VALUES (true, 2, 'manual', 'owner', 'owner:hem@example.invalid', 'r', '{}'::jsonb)",
            Ct);
        var readerString = await ReaderConnectionAsync();
        await using var readOnly = NpgsqlDataSource.Create(readerString);
        var before = (await CountAsync("research.extension_evaluations"), await CountAsync("filings.event_filter_decisions"));

        var result = await Handler(readOnly).HandleAsync(new EventsPhaseCheckQuery([]), Ct);

        result.IsSuccess.ShouldBeTrue();
        var report = result.Value.Report;
        report.Verdict.ShouldBe("NOT MET");
        var violation = report.Violations.ShouldHaveSingleItem();
        (violation.DecisionId, violation.DecidedBy).ShouldBe((1L, "owner"));
        report.NoUpcomingShare.ShouldBe(0.25);
        report.Families.Single(f => f.Family == ExtensionFamilies.Filter).Status.ShouldBe("DROP");
        report.Families.Where(f => f.Family != ExtensionFamilies.Filter).ShouldAllBe(f => f.Status == "none");
        (await CountAsync("research.extension_evaluations"), await CountAsync("filings.event_filter_decisions")).ShouldBe(before);
        await ShouldBeRefusedAsync(readerString, "INSERT INTO filings.event_filter_decisions (enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot) VALUES (false, 2, 'manual', 'owner', 'owner:x@example.invalid', 'r', '{}'::jsonb)");
        await ShouldBeRefusedAsync(readerString, "DELETE FROM research.extension_evaluations");
    }

    [Fact]
    public async Task The_same_rows_give_the_same_text_and_a_missing_table_is_not_evaluable_not_an_error()
    {
        await StoreKit.ExecuteAsync(server.Owner, "TRUNCATE research.extension_evaluations; TRUNCATE filings.event_filter_decisions RESTART IDENTITY", Ct);
        await using var app = NpgsqlDataSource.Create(server.App);
        await new ExtensionEvaluationStore(app).AppendAsync(Filter(new string('2', 64), "KEEP"), Now, Ct);
        var readerString = await ReaderConnectionAsync();
        await using var readOnly = NpgsqlDataSource.Create(readerString);
        var first = await Handler(readOnly).HandleAsync(new EventsPhaseCheckQuery([]), Ct);
        var second = await Handler(readOnly).HandleAsync(new EventsPhaseCheckQuery([]), Ct);
        first.Value.Text.ShouldBe(second.Value.Text);
        first.Value.Json.ShouldBe(second.Value.Json);

        await StoreKit.ExecuteAsync(server.Owner, "ALTER TABLE research.extension_evaluations RENAME TO extension_evaluations_hidden", Ct);
        try
        {
            await using var again = NpgsqlDataSource.Create(readerString);
            var missing = await Handler(again).HandleAsync(new EventsPhaseCheckQuery([]), Ct);

            missing.IsSuccess.ShouldBeTrue();
            missing.Value.Report.Verdict.ShouldBe("NOT EVALUABLE");
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
