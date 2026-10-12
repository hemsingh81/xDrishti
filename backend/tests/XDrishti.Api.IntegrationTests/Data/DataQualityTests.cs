using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The data quality checks and their fix actions on real TimescaleDB (ADR 0017 §4, P1-14a): bars loaded from CSV (the same pipeline as everywhere), a fake Dhan source behind the port, the
/// background job run by the test. The clock is Monday 2026-10-12 12:00 India time, so the window is Monday 5 to Friday 9 October; partial day: Tuesday the 6th.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataQualityTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Partial = new(2026, 10, 6);

    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static readonly DateOnly[] Sessions =
        [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 5), Partial, new(2026, 10, 7), new(2026, 10, 8), Friday];

    private const int PartialBars = 200;

    /// <summary>Loads the sessions of an instrument from CSV and aggregates them, as the pipeline does. <paramref name="partialDay"/> cuts the 6th short.</summary>
    private async Task<Guid> LoadAsync(string ticker, int seed, IEnumerable<DateOnly>? sessions = null, bool partialDay = false, int fridayBars = SessionClock.RegularBars)
    {
        var id = await SeedAsync(ticker);
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var bars = (sessions ?? Sessions).SelectMany(d => BarFixtures.Session(d, seed, count: partialDay && d == Partial ? PartialBars : d == Friday ? fridayBars : SessionClock.RegularBars)).ToList();
        WriteCsv(ticker, bars);
        var job = await RunAsync(["csv", "aggregate"]);
        job.Status.ShouldBe("succeeded", job.Error);
        return id;
    }

    /// <summary>"Run checks": posts, lets the worker's job run, returns the finished job. The clock moves on a second first so runs are ordered.</summary>
    private async Task<DataJobView> CheckAsync(Guid? instrumentId = null)
    {
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await Client.PostJsonAsync("/api/data/quality/run", instrumentId is { } id ? new { instrumentId = id } : new { }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        response.Headers.Location!.ToString().ShouldBe($"/api/data/jobs/{started.Id}");
        return await FinishAsync(started.Id);
    }

    private async Task<DataJobView> FinishAsync(Guid jobId)
    {
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(jobId);
        job.Status.ShouldNotBe("running");
        return job;
    }

    private async Task<DataJobView> ActionAsync(Guid id, string action)
    {
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await Client.PostJsonAsync($"/api/data/quality/instruments/{id}/{action}", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return await FinishAsync((await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!.Id);
    }

    private async Task<QualityOverview> QualityOverviewAsync()
    {
        using var response = await Client.GetAsync("/api/data/quality/overview", Ct);
        return await response.ReadAsync<QualityOverview>(Ct);
    }

    private async Task<QualityInstrumentPage> QualityInstrumentsAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/quality/instruments" + query, Ct);
        return await response.ReadAsync<QualityInstrumentPage>(Ct);
    }

    private async Task<QualityInstrumentDetail> QualityDetailAsync(Guid id, string query = "")
    {
        using var response = await Client.GetAsync($"/api/data/quality/instruments/{id}{query}", Ct);
        return await response.ReadAsync<QualityInstrumentDetail>(Ct);
    }

    private async Task<List<QualityRunView>> QualityRunsAsync()
    {
        using var response = await Client.GetAsync("/api/data/quality/runs", Ct);
        return await response.ReadAsync<List<QualityRunView>>(Ct);
    }

    private async Task<QualityGateView> GateAsync()
    {
        using var response = await Client.GetAsync("/api/data/quality/gate", Ct);
        return await response.ReadAsync<QualityGateView>(Ct);
    }

    private async Task<DataNotices> NoticesAsync()
    {
        using var response = await Client.GetAsync("/api/data/notices", Ct);
        return await response.ReadAsync<DataNotices>(Ct);
    }

    private async Task TamperAsync(Guid id, string timeframe, DateOnly date, int minuteOfDay, string set)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"UPDATE market.bars_agg SET {set} WHERE instrument_id = @id AND timeframe = @timeframe AND ts = to_timestamp(@ts)",
            ("id", id), ("timeframe", timeframe), ("ts", (double)BarFixtures.Epoch(date, minuteOfDay)));
    }

    [Fact]
    public async Task Before_any_run_nothing_is_checked_and_the_gate_is_closed()
    {
        await LoadAsync("ABB", 1);

        var overview = await QualityOverviewAsync();

        overview.LastRun.ShouldBeNull();
        overview.Counts.ShouldBe(new QualityCounts(1, 0, 0, 0, 0, 1, null));
        (overview.Gate.Ok, overview.Gate.Blocked).ShouldBe((false, true));
        overview.Gate.Reasons.ShouldBe(["There is no completed data quality run over every instrument yet."]);
        overview.Window.ShouldBe(new QualityWindowView(5, new DateOnly(2026, 10, 5), Friday));
        (overview.SpikeMultiple, overview.MaxMissingPercent).ShouldBe((20.0, 1.0));
        var row = (await QualityInstrumentsAsync()).Items.ShouldHaveSingleItem();
        (row.Status, row.Score, row.Checks).ShouldBe(("notChecked", null, null));
        (await QualityRunsAsync()).ShouldBeEmpty();
        (await NoticesAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_checks_over_clean_data_passes_every_instrument_and_records_the_run_the_table_the_detail_and_an_open_gate()
    {
        var abb = await LoadAsync("ABB", 1);
        await LoadAsync("TCS", 2);

        var job = await CheckAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        job.Steps.ShouldBe(["quality"]);
        (job.Quality!.Status, job.Quality.Done, job.Quality.Total, job.Quality.Passed, job.Quality.Warned, job.Quality.Failed).ShouldBe(("succeeded", 2, 2, 2, 0, 0));
        (job.Csv.Status, job.Dhan.Status, job.Aggregate.Status).ShouldBe(("notRequested", "notRequested", "notRequested"));

        var overview = await QualityOverviewAsync();
        var run = overview.LastRun!;
        (run.Id, run.Trigger, run.Status, run.Sessions, run.Instruments, run.Passed, run.Warned, run.Failed, run.Score, run.InstrumentId, run.JobId).ShouldBe((job.Quality.RunId!.Value, "manual", "succeeded", 5, 2, 2, 0, 0, 100.0, null, job.Id));
        overview.Counts.ShouldBe(new QualityCounts(2, 2, 2, 0, 0, 0, 100));
        (overview.Gate.Ok, overview.Gate.Blocked).ShouldBe((true, false));
        overview.Gate.Reasons.ShouldBeEmpty();
        var gate = await GateAsync();
        (gate.Ok, gate.Blocked, gate.CheckedInstruments, gate.FailedInstruments, gate.MaxFailedPercent).ShouldBe((true, false, 2, 0, 5.0));

        var page = await QualityInstrumentsAsync();
        page.Total.ShouldBe(2);
        var row = page.Items.Single(i => i.Symbol == "NSE:ABB");
        (row.Status, row.Score, row.Coverage, row.BarsChecked, row.MissingMinutes, row.Source, row.RunId).ShouldBe(("pass", 100, 100.0, 1875, 0, "csv", run.Id));
        row.Aggregates!.Keys.ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        row.Aggregates.Values.ShouldAllBe(s => s == "pass");
        (row.Checks!.Completeness, row.Checks.Sanity, row.Checks.Spikes, row.Checks.Aggregates, row.Checks.Freshness, row.Checks.OfficialDaily).ShouldBe(("pass", "pass", "pass", "pass", "pass", "notAvailable"));
        row.Issues.ShouldBeEmpty();

        var detail = await QualityDetailAsync(abb);
        (detail.Symbol, detail.Status, detail.Score, detail.RunId, detail.Missing.Total).ShouldBe(("NSE:ABB", "pass", 100, run.Id, 0));
        detail.Checks!.Completeness.Sessions.Select(s => s.Date).ShouldBe([new DateOnly(2026, 10, 5), Partial, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 8), Friday]);
        detail.Checks.Aggregates.ByTimeframe["1D"].Compared.ShouldBe(5);
        detail.Checks.OfficialDaily.Status.ShouldBe("notAvailable");
        detail.Checks.Freshness.ExpectedSession.ShouldBe(Friday);
        (detail.Buckets.Differences, detail.Buckets.Truncated).ShouldBe((0, false));
        detail.Buckets.Items.ShouldAllBe(r => r.Stored != null && r.Recomputed != null && !r.Differs);

        var runs = await QualityRunsAsync();
        runs.ShouldHaveSingleItem().Id.ShouldBe(run.Id);
        (await NoticesAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tampered_aggregate_fails_the_instrument_is_listed_by_bucket_and_Refresh_aggregates_repairs_it_and_checks_it_again()
    {
        var abb = await LoadAsync("ABB", 1);
        await LoadAsync("TCS", 2);
        await TamperAsync(abb, "15m", new DateOnly(2026, 10, 7), SessionClock.OpenMinute + 45, "high = high + 1");
        await TamperAsync(abb, "1D", new DateOnly(2026, 10, 8), 0, "volume = volume + 7");

        var job = await CheckAsync();

        (job.Quality!.Passed, job.Quality.Failed).ShouldBe((1, 1));
        var failing = (await QualityInstrumentsAsync("?status=fail")).Items.ShouldHaveSingleItem();
        (failing.Symbol, failing.Status, failing.Score).ShouldBe(("NSE:ABB", "fail", 75));
        failing.Aggregates!["15m"].ShouldBe("fail");
        failing.Aggregates["5m"].ShouldBe("pass");
        failing.Issues.ShouldBe(["2 aggregate bars differ from the 1-minute bars"]);
        (await QualityInstrumentsAsync("?status=pass")).Items.Single().Symbol.ShouldBe("NSE:TCS");
        var detail = await QualityDetailAsync(abb, "?differencesOnly=true");
        detail.Buckets.Items.Count.ShouldBe(2);
        detail.Buckets.Items.Select(r => (r.Timeframe, string.Join(',', r.Fields))).ShouldBe([("15m", "high"), ("1D", "volume")]);
        detail.Buckets.Items[0].Stored!.High.ShouldBe(detail.Buckets.Items[0].Recomputed!.High + 1);
        (detail.Buckets.Total, detail.Buckets.Differences).ShouldBe(((await QualityDetailAsync(abb)).Buckets.Total, 2));
        (await QualityDetailAsync(abb, "?timeframe=1D&differencesOnly=true")).Buckets.Items.ShouldHaveSingleItem();
        var gate = await GateAsync();
        (gate.Blocked, gate.FailedInstruments, gate.FailedPercent).ShouldBe((true, 1, 50.0));
        var notice = (await NoticesAsync()).Items.ShouldHaveSingleItem();
        (notice.Kind, notice.Severity).ShouldBe(("data_quality_failed", "error"));
        notice.Text.ShouldBe("Data quality blocks the nightly plan: 1 of 2 instruments fail the checks (50 %), more than the 5 % allowed.");

        var fix = await ActionAsync(abb, "refresh-aggregates");

        fix.Status.ShouldBe("succeeded", fix.Error);
        fix.InstrumentId.ShouldBe(abb);
        fix.Steps.ShouldBe(["aggregate", "quality"]);
        (fix.Aggregate.Status, fix.Aggregate.Done, fix.Aggregate.Full).ShouldBe(("succeeded", 1, true));
        (fix.Quality!.Total, fix.Quality.Passed).ShouldBe((1, 1));
        (await QualityInstrumentsAsync("?q=abb")).Items.Single().Status.ShouldBe("pass");
        (await QualityDetailAsync(abb)).Buckets.Differences.ShouldBe(0);
        var runs = await QualityRunsAsync();
        runs.Count.ShouldBe(2);
        (runs[0].InstrumentId, runs[0].Trigger, runs[0].Instruments, runs[0].Passed).ShouldBe((abb, "after_job", 1, 1));
        var overview = await QualityOverviewAsync();
        overview.LastRun!.InstrumentId.ShouldBeNull(); // the header still shows the last run over every instrument
        overview.Counts.ShouldBe(new QualityCounts(2, 2, 2, 0, 0, 0, 100));
        (await GateAsync()).Ok.ShouldBeTrue(); // the gate reads the current result of every instrument, not the old run
        (await NoticesAsync()).Items.ShouldBeEmpty();
    }
}
