using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The fix actions of the Data quality screen, end to end on real TimescaleDB with a fake Dhan source behind the port: what a re-fetch and a refresh of the aggregates do to the data, the gap
/// records and the verdict; late data; stale instruments and holidays; and the one-job-at-a-time rule between quality runs and data jobs.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataQualityFixActionsTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Partial = new(2026, 10, 6);

    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static readonly DateOnly[] Sessions =
        [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 5), Partial, new(2026, 10, 7), new(2026, 10, 8), Friday];

    private const int PartialBars = 200;

    private static readonly int FirstMissing = SessionClock.OpenMinute + PartialBars;

    private static bool IsPartialDayWindow(FetchWindow w) => w.From.ToUnixTimeSeconds() == BarFixtures.Epoch(Partial, 0) && w.To.ToUnixTimeSeconds() == BarFixtures.Epoch(Partial.AddDays(1), 0);

    private static List<Bar> MissingOfPartialDay(int seed) => BarFixtures.Session(Partial, seed, startMinute: FirstMissing, count: SessionClock.RegularBars - PartialBars);

    private async Task<Guid> LoadAsync(string ticker, int seed, IEnumerable<DateOnly>? sessions = null, bool partialDay = false, int fridayBars = SessionClock.RegularBars)
    {
        var id = await SeedAsync(ticker);
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        WriteCsv(ticker, [.. (sessions ?? Sessions).SelectMany(d => BarFixtures.Session(d, seed, count: partialDay && d == Partial ? PartialBars : d == Friday ? fridayBars : SessionClock.RegularBars))]);
        (await RunAsync(["csv", "aggregate"])).Status.ShouldBe("succeeded");
        return id;
    }

    private async Task<DataJobView> FinishAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(started.Id);
        job.Status.ShouldNotBe("running");
        return job;
    }

    private async Task<DataJobView> CheckAsync(Guid? instrumentId = null)
    {
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await Client.PostJsonAsync("/api/data/quality/run", instrumentId is { } id ? new { instrumentId = id } : new { }, Ct);
        return await FinishAsync(response);
    }

    private async Task<DataJobView> ActionAsync(Guid id, string action)
    {
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await Client.PostJsonAsync($"/api/data/quality/instruments/{id}/{action}", null, Ct);
        return await FinishAsync(response);
    }

    private async Task<QualityInstrumentRow> RowAsync(string query)
    {
        using var response = await Client.GetAsync("/api/data/quality/instruments" + query, Ct);
        return (await response.ReadAsync<QualityInstrumentPage>(Ct)).Items.ShouldHaveSingleItem();
    }

    private async Task<QualityInstrumentDetail> DetailAsync(Guid id)
    {
        using var response = await Client.GetAsync($"/api/data/quality/instruments/{id}", Ct);
        return await response.ReadAsync<QualityInstrumentDetail>(Ct);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.ReadProblemAsync(TestContext.Current.CancellationToken)).Code;
    }

    private async Task<GapPage> GapsAsync()
    {
        using var response = await Client.GetAsync("/api/data/gaps", Ct);
        return await response.ReadAsync<GapPage>(Ct);
    }

    [Fact]
    public async Task A_partial_day_warns_and_Re_fetch_gets_the_missing_bars_from_dhan_rebuilds_the_aggregates_and_checks_the_instrument_again()
    {
        var abb = await LoadAsync("ABB", 1, partialDay: true);
        var source = Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. t.Windows.Any(IsPartialDayWindow) ? MissingOfPartialDay(1) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();
        await CheckAsync();
        var before = await DetailAsync(abb);
        (before.Status, before.Checks!.Completeness.MissingMinutes, before.Checks.Completeness.MissingPercent).ShouldBe(("warning", 175, 9.33));
        before.Missing.Total.ShouldBe(175);
        before.Missing.Items[0].ShouldBe(new MissingMinute(Partial, "12:35"));
        before.Missing.Items[^1].ShouldBe(new MissingMinute(Partial, "15:29"));

        var fix = await ActionAsync(abb, "refetch");

        fix.Status.ShouldBe("succeeded", fix.Error);
        fix.Steps.ShouldBe(["dhan", "aggregate", "quality"]);
        (fix.InstrumentId, fix.RefetchGaps).ShouldBe((abb, true));
        (fix.Dhan.GapsFound, fix.Dhan.GapsFilled, fix.Dhan.Inserted).ShouldBe((1, 1, SessionClock.RegularBars - PartialBars));
        source.Plans.ShouldHaveSingleItem().Targets.ShouldHaveSingleItem().Windows.Any(IsPartialDayWindow).ShouldBeTrue();
        (fix.Aggregate.Status, fix.Quality!.Status, fix.Quality.Total).ShouldBe(("succeeded", "succeeded", 1));
        var after = await DetailAsync(abb);
        (after.Status, after.Checks!.Completeness.MissingMinutes, after.Missing.Total, after.Source).ShouldBe(("pass", 0, 0, "mixed"));
        (after.Buckets.Differences, after.Checks.Aggregates.Differences).ShouldBe((0, 0));
        (await GapsAsync()).Items.ShouldHaveSingleItem().Status.ShouldBe("filled");
        (await RowAsync("?q=abb")).Status.ShouldBe("pass");
    }

    [Fact]
    public async Task A_day_dhan_cannot_fill_becomes_unfillable_and_stays_a_warning_with_those_minutes_named()
    {
        var abb = await LoadAsync("ABB", 1, partialDay: true);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        var fix = await ActionAsync(abb, "refetch");

        fix.Status.ShouldBe("succeeded", fix.Error);
        (fix.Dhan.GapsFound, fix.Dhan.GapsUnfillable).ShouldBe((1, 1));
        (await GapsAsync()).Items.ShouldHaveSingleItem().Status.ShouldBe("unfillable");
        var detail = await DetailAsync(abb);
        (detail.Status, detail.Checks!.Completeness.UnfillableMinutes).ShouldBe(("warning", 175));
        detail.Checks.Completeness.Sessions.Single(s => s.Date == Partial).Unfillable.ShouldBeTrue();
        (await RowAsync("?q=abb")).Issues.ShouldHaveSingleItem().ShouldBe("175 missing minutes (9.33 %), 175 of them could not be fetched");
    }

    [Fact]
    public async Task A_small_gap_below_the_limit_passes_but_an_unfillable_one_does_not()
    {
        var abb = await LoadAsync("ABB", 1, fridayBars: SessionClock.RegularBars - 5);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        await CheckAsync();
        var small = await DetailAsync(abb);
        await ActionAsync(abb, "refetch");
        var afterRefetch = await DetailAsync(abb);

        (small.Status, small.Checks!.Completeness.MissingMinutes, small.Checks.Completeness.MissingPercent).ShouldBe(("pass", 5, 0.27));
        (afterRefetch.Status, afterRefetch.Checks!.Completeness.UnfillableMinutes).ShouldBe(("warning", 5));
    }

    [Fact]
    public async Task Late_data_written_without_aggregating_is_caught_by_the_comparison_and_Refresh_aggregates_fixes_it()
    {
        var abb = await LoadAsync("ABB", 1, fridayBars: SessionClock.RegularBars - 10);
        foreach (var bar in BarFixtures.Session(Friday, 1, startMinute: SessionClock.OpenMinute + SessionClock.RegularBars - 10, count: 10))
        {
            await StoreAsync(abb, bar, "dhan"); // late bars arrive; nobody refreshes the aggregates
        }

        await CheckAsync();

        var stale = await DetailAsync(abb);
        (stale.Status, stale.Checks!.Aggregates.Status, stale.Checks.Completeness.MissingMinutes).ShouldBe(("fail", "fail", 0));
        stale.Checks.Aggregates.ByTimeframe.Values.Where(t => t.Differences > 0).Count().ShouldBeGreaterThanOrEqualTo(6);
        stale.Buckets.Items.Single(b => b.Differs && b.Timeframe == "1D").Fields.ShouldContain("bars");
        stale.Buckets.Items.Where(b => b.Differs).SelectMany(b => b.Fields).ShouldContain("missing");

        var fix = await ActionAsync(abb, "refresh-aggregates");

        fix.Status.ShouldBe("succeeded", fix.Error);
        var fixedUp = await DetailAsync(abb);
        (fixedUp.Status, fixedUp.Checks!.Aggregates.Differences, fixedUp.Buckets.Differences).ShouldBe(("pass", 0, 0));
    }

    [Fact]
    public async Task An_instrument_that_stopped_before_the_last_session_fails_on_freshness_until_the_calendar_says_the_market_was_closed()
    {
        var tcs = await LoadAsync("TCS", 2, Sessions.Where(d => d != Friday));

        await CheckAsync();
        var behind = await DetailAsync(tcs);
        using var holiday = await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-09", kind = "holiday", note = "test" }, Ct);
        holiday.EnsureSuccessStatusCode();
        await CheckAsync();
        var closed = await DetailAsync(tcs);

        (behind.Status, behind.Checks!.Freshness.Status, behind.Checks.Freshness.DaysBehind, behind.Checks.Freshness.ExpectedSession).ShouldBe(("fail", "fail", 1, Friday));
        behind.Checks.Completeness.MissingMinutes.ShouldBe(375);
        (closed.Status, closed.Checks!.Freshness.Status, closed.Checks.Freshness.ExpectedSession).ShouldBe(("pass", "pass", new DateOnly(2026, 10, 8)));
        closed.Checks.Completeness.ExpectedMinutes.ShouldBe(5 * 375);
        (await RowAsync("?q=tcs")).CheckedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task While_a_data_job_runs_quality_runs_and_fix_actions_are_refused_and_while_one_runs_so_is_every_data_job()
    {
        var abb = await LoadAsync("ABB", 1);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        var running = await StartAsync(["aggregate"]);
        using var run = await Client.PostJsonAsync("/api/data/quality/run", new { }, Ct);
        using var refetch = await Client.PostJsonAsync($"/api/data/quality/instruments/{abb}/refetch", null, Ct);
        using var refresh = await Client.PostJsonAsync($"/api/data/quality/instruments/{abb}/refresh-aggregates", null, Ct);
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);

        (await ProblemCodeAsync(run, HttpStatusCode.Conflict)).ShouldBe("data.job_running");
        (await ProblemCodeAsync(refetch, HttpStatusCode.Conflict)).ShouldBe("data.job_running");
        (await ProblemCodeAsync(refresh, HttpStatusCode.Conflict)).ShouldBe("data.job_running");
        (await GetJobAsync(running.Id)).Status.ShouldBe("succeeded");

        using var started = await Client.PostJsonAsync("/api/data/quality/run", new { }, Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var update = await PostRefreshAsync();
        (await ProblemCodeAsync(update, HttpStatusCode.Conflict)).ShouldBe("data.job_running");
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        using var again = await PostRefreshAsync(["aggregate"]);
        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Re_fetch_is_refused_when_dhan_cannot_be_asked_and_nothing_is_started()
    {
        var abb = await LoadAsync("ABB", 1);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], t.Windows.Count));
        Factory.Dhan.UseAccount(new DataAccountState(null, null, DataAccountStatuses.None, "There is no connected primary Data account."));

        using var response = await Client.PostJsonAsync($"/api/data/quality/instruments/{abb}/refetch", null, Ct);

        (await ProblemCodeAsync(response, HttpStatusCode.Conflict)).ShouldBe("data.dhan_not_connected");
        Factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_instrument_is_not_found_by_every_route_that_names_one()
    {
        var id = Guid.NewGuid();

        using var run = await Client.PostJsonAsync("/api/data/quality/run", new { instrumentId = id }, Ct);
        using var refetch = await Client.PostJsonAsync($"/api/data/quality/instruments/{id}/refetch", null, Ct);
        using var refresh = await Client.PostJsonAsync($"/api/data/quality/instruments/{id}/refresh-aggregates", null, Ct);
        using var detail = await Client.GetAsync($"/api/data/quality/instruments/{id}", Ct);

        foreach (var response in new[] { run, refetch, refresh, detail })
        {
            (await ProblemCodeAsync(response, HttpStatusCode.NotFound)).ShouldBe("data.instrument_not_found");
        }

        Factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("?status=bogus")]
    [InlineData("?sort=bogus")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=500")]
    public async Task A_bad_table_search_is_a_validation_problem(string query)
    {
        using var response = await Client.GetAsync("/api/data/quality/instruments" + query, Ct);

        (await ProblemCodeAsync(response, HttpStatusCode.BadRequest)).ShouldBe("data.invalid");
    }

    [Fact]
    public async Task A_timeframe_that_is_not_an_aggregate_is_a_validation_problem()
    {
        var abb = await LoadAsync("ABB", 1);

        using var response = await Client.GetAsync($"/api/data/quality/instruments/{abb}?timeframe=1m", Ct);

        (await ProblemCodeAsync(response, HttpStatusCode.BadRequest)).ShouldBe("data.invalid");
    }

    [Fact]
    public async Task The_table_filters_sorts_and_pages_and_leaves_out_instruments_without_bars()
    {
        await LoadAsync("ABB", 1);
        await LoadAsync("TCS", 2, Sessions.Where(d => d != Friday));
        await LoadAsync("INFY", 3, partialDay: true);
        var empty = await SeedAsync("EMPTY");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { empty } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        await CheckAsync();

        async Task<List<string>> SymbolsAsync(string query)
        {
            using var response = await Client.GetAsync("/api/data/quality/instruments" + query, Ct);
            return [.. (await response.ReadAsync<QualityInstrumentPage>(Ct)).Items.Select(i => i.Symbol)];
        }

        (await SymbolsAsync(string.Empty)).ShouldBe(["NSE:ABB", "NSE:INFY", "NSE:TCS"]);
        (await SymbolsAsync("?status=pass")).ShouldBe(["NSE:ABB"]);
        (await SymbolsAsync("?status=warning")).ShouldBe(["NSE:INFY"]);
        (await SymbolsAsync("?status=fail")).ShouldBe(["NSE:TCS"]);
        (await SymbolsAsync("?status=notChecked")).ShouldBeEmpty();
        (await SymbolsAsync("?sort=-status")).ShouldBe(["NSE:TCS", "NSE:INFY", "NSE:ABB"]);
        (await SymbolsAsync("?sort=score")).ShouldBe(["NSE:TCS", "NSE:INFY", "NSE:ABB"]);
        (await SymbolsAsync("?sort=-coverage")).First().ShouldBe("NSE:ABB");
        (await SymbolsAsync("?sort=-missingMinutes")).First().ShouldBe("NSE:TCS");
        (await SymbolsAsync("?q=inf")).ShouldBe(["NSE:INFY"]);
        (await SymbolsAsync("?pageSize=2&page=2")).ShouldBe(["NSE:TCS"]);
        using var response = await Client.GetAsync("/api/data/quality/overview", Ct);
        var counts = (await response.ReadAsync<QualityOverview>(Ct)).Counts;
        (counts.Tracked, counts.Checked, counts.Passed, counts.Warned, counts.Failed, counts.NotChecked).ShouldBe((3, 3, 1, 1, 1, 0));
        counts.Score.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_quality_step_can_be_part_of_a_manual_update_and_is_then_recorded_after_job_but_a_default_update_leaves_it_out()
    {
        await LoadAsync("ABB", 1);

        var plain = await RunAsync();
        var withQuality = await RunAsync(["aggregate", "quality"]);

        plain.Steps.ShouldBe(["csv", "dhan", "aggregate"]);
        plain.Quality.ShouldBeNull();
        withQuality.Status.ShouldBe("succeeded", withQuality.Error);
        withQuality.Quality!.Status.ShouldBe("succeeded");
        using var runs = await Client.GetAsync("/api/data/quality/runs", Ct);
        var run = (await runs.ReadAsync<List<QualityRunView>>(Ct)).ShouldHaveSingleItem();
        (run.Trigger, run.JobId).ShouldBe(("after_job", withQuality.Id));
    }

    [Fact]
    public async Task A_job_that_the_application_left_running_is_failed_at_start_up_with_its_quality_run_and_the_next_run_can_start()
    {
        var abb = await LoadAsync("ABB", 1);
        var staleJob = Guid.CreateVersion7();
        var staleRun = Guid.CreateVersion7();
        var longAgo = Now.AddHours(-2);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO market.data_jobs (id, requested_by, trigger, started_at, status, steps, force_csv, refetch_gaps, rebuild_all, csv, dhan, aggregate, quality, warnings)
                VALUES (@job, 'owner', 'manual', @at, 'running', '["quality"]'::jsonb, false, false, false,
                        '{"status":"notRequested"}'::jsonb, '{"status":"notRequested"}'::jsonb, '{"status":"notRequested"}'::jsonb, '{"status":"running"}'::jsonb, '[]'::jsonb);
                INSERT INTO market.quality_runs (id, trigger, status, started_at, sessions, instruments, passed, warned, failed, job_id)
                VALUES (@run, 'manual', 'running', @at, 5, 1, 0, 0, 0, @job);
                """,
                ("job", staleJob), ("run", staleRun), ("at", longAgo));
        }

        using (var blocked = await Client.PostJsonAsync("/api/data/quality/run", new { }, Ct))
        {
            (await ProblemCodeAsync(blocked, HttpStatusCode.Conflict)).ShouldBe("data.job_running");
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var recover = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedDataJobsCommand>>();
            (await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now), Ct)).IsSuccess.ShouldBeTrue();
        }

        (await ScalarAsync<string>("SELECT status FROM market.data_jobs WHERE id = @id", ("id", staleJob))).ShouldBe("failed");
        (await ScalarAsync<string>("SELECT status FROM market.quality_runs WHERE id = @id", ("id", staleRun))).ShouldBe("failed");
        (await ScalarAsync<string>("SELECT error FROM market.quality_runs WHERE id = @id", ("id", staleRun))).ShouldContain("did not finish");
        var job = await CheckAsync(abb);
        job.Status.ShouldBe("succeeded", job.Error);
        using var gate = await Client.GetAsync("/api/data/quality/gate", Ct);
        (await gate.ReadAsync<QualityGateView>(Ct)).Blocked.ShouldBeTrue(); // the only run over every instrument failed: fail-closed
    }

    [Fact]
    public async Task A_quality_run_left_running_by_a_job_that_ended_does_not_block_the_next_run()
    {
        await LoadAsync("ABB", 1);
        var endedJob = Guid.CreateVersion7();
        var orphan = Guid.CreateVersion7();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO market.data_jobs (id, requested_by, trigger, started_at, finished_at, status, steps, force_csv, refetch_gaps, rebuild_all, csv, dhan, aggregate, quality, warnings)
                VALUES (@job, 'owner', 'manual', @at, @at, 'failed', '["quality"]'::jsonb, false, false, false,
                        '{"status":"notRequested"}'::jsonb, '{"status":"notRequested"}'::jsonb, '{"status":"notRequested"}'::jsonb, '{"status":"failed"}'::jsonb, '[]'::jsonb);
                INSERT INTO market.quality_runs (id, trigger, status, started_at, sessions, instruments, passed, warned, failed, job_id)
                VALUES (@run, 'manual', 'running', @at, 5, 1, 0, 0, 0, @job);
                """,
                ("job", endedJob), ("run", orphan), ("at", Now.AddHours(-1)));
        }

        var job = await CheckAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        (await ScalarAsync<string>("SELECT status FROM market.quality_runs WHERE id = @id", ("id", orphan))).ShouldBe("failed");
    }
}
