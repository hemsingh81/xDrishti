using System.Net;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The scheduled end-of-day run against the real database (ADR 0017 §2, P1-12a, ADR 0018): the scheduler is asked to look at pinned instants (its hosted loop is switched off for the suite), the job
/// it starts is the same one the endpoint starts, with a fake Dhan source behind the port. The service starts paused (<c>Data:Eod:Enabled=false</c> seeds it so) and each test resumes it on the Services
/// screen's endpoint. Days: Mon 2026-10-12 to Wed 2026-10-14.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataEodRunTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly string[] WeekDays = ["mon", "tue", "wed", "thu", "fri"];

    private static readonly DateOnly Partial = new(2026, 10, 6);

    private static readonly DateOnly[] Sessions =
        [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 5), Partial, new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9)];

    private ServiceScheduler Scheduler => Factory.Services.GetRequiredService<ServiceScheduler>();

    private ServiceRunner Runner => Factory.Services.GetRequiredService<ServiceRunner>();

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, Ist);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        using var resumed = await Client.PostAsync("/api/services/eod/resume", Ct);
        resumed.EnsureSuccessStatusCode();
    }

    /// <summary>The scheduler looks at the instant (after a look a minute before, so that the instant is the one that came since the last look) and the data jobs it starts are run to the end.</summary>
    private async Task TickAtAsync(DateTimeOffset instant)
    {
        await TickWithoutRunningJobsAsync(instant);
        await PumpJobsAsync();
    }

    private async Task TickWithoutRunningJobsAsync(DateTimeOffset instant)
    {
        Factory.Clock.Pin(instant - TimeSpan.FromMinutes(1));
        await Scheduler.TickAsync(Ct);
        Factory.Clock.Pin(instant);
        await ResignInAsync();
        await Scheduler.TickAsync(Ct);
    }

    /// <summary>Runs the registered data jobs while the service run waits for them, then waits for the run to end.</summary>
    private async Task PumpJobsAsync()
    {
        var idle = Runner.WhenIdleAsync();
        while (!idle.IsCompleted)
        {
            await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
            await Task.WhenAny(idle, Task.Delay(20, Ct));
        }

        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
    }

    private async Task<List<ServiceRunView>> EodRunsAsync()
    {
        using var response = await Client.GetAsync("/api/services/eod/runs?pageSize=100", Ct);
        return (await response.ReadAsync<ServiceRunPage>(Ct)).Items.OrderBy(r => r.StartedAt).ToList();
    }

    private async Task<List<DataJobView>> JobsAsync()
    {
        using var response = await Client.GetAsync("/api/data/jobs", Ct);
        return await response.ReadAsync<List<DataJobView>>(Ct);
    }

    /// <summary>The scheduled jobs only, newest first (a test may have started others to arrange its data).</summary>
    private async Task<List<DataJobView>> DailyJobsAsync() => [.. (await JobsAsync()).Where(j => j.Trigger == "daily")];

    private async Task<ServiceNotices> ServiceNoticesAsync()
    {
        using var response = await Client.GetAsync("/api/services/notices", Ct);
        return await response.ReadAsync<ServiceNotices>(Ct);
    }

    private async Task<DataNotices> NoticesAsync()
    {
        using var response = await Client.GetAsync("/api/data/notices", Ct);
        return await response.ReadAsync<DataNotices>(Ct);
    }

    private async Task<Guid> TrackedWithHistoryAsync(string ticker, int seed)
    {
        var id = await SeedAsync(ticker);
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        WriteCsv(ticker, [.. Sessions.SelectMany(d => BarFixtures.Session(d, seed, count: d == Partial ? 200 : SessionClock.RegularBars))]);
        (await RunAsync(["csv", "aggregate"])).Status.ShouldBe("succeeded");
        return id;
    }

    [Fact]
    public async Task A_week_of_automatic_runs_fetches_backfills_flags_open_gaps_gives_up_on_them_and_never_runs_twice_a_day()
    {
        var abb = await TrackedWithHistoryAsync("ABB", 1);
        var day = 12;
        var served = 375;
        var source = Factory.Dhan.UseSource(t => new IntradayResult(
            t.InstrumentId,
            [.. t.Windows.SelectMany(w => Serve(w, day, served))],
            t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        // Before the time: nothing. At 15:50 on Monday: one scheduled job; it backfills the partial day of the 6th and fetches the whole of today.
        await TickAtAsync(At(12, 15, 49));
        (await DailyJobsAsync()).ShouldBeEmpty();
        await TickAtAsync(At(12, 15, 50));

        var monday = (await DailyJobsAsync()).ShouldHaveSingleItem();
        monday.Status.ShouldBe("succeeded", monday.Error);
        (monday.Trigger, monday.RequestedBy, monday.RefetchGaps).ShouldBe(("daily", "system:data-eod", false));
        monday.Steps.ShouldBe(["dhan", "aggregate", "quality"]);
        (monday.Quality!.Status, monday.Quality.Total, monday.Quality.Done).ShouldBe(("succeeded", 1, 1));
        using (var runs = await Client.GetAsync("/api/data/quality/runs", Ct))
        {
            var run = (await runs.ReadAsync<List<XDrishti.Application.Market.Quality.QualityRunView>>(Ct)).ShouldHaveSingleItem();
            (run.Id, run.Trigger, run.Status, run.JobId).ShouldBe((monday.Quality.RunId!.Value, "daily", "succeeded", monday.Id));
        }

        (monday.Dhan.GapsFound, monday.Dhan.GapsFilled, monday.Dhan.GapsUnfillable).ShouldBe((1, 1, 0));
        (await GapsStatusesAsync()).ShouldBe([("2026-10-06", "filled")]);
        await TickAtAsync(At(12, 17, 0));
        (await DailyJobsAsync()).Count.ShouldBe(1);
        var mondayRun = (await EodRunsAsync()).ShouldHaveSingleItem();
        (mondayRun.Trigger, mondayRun.Status, mondayRun.DataJobId).ShouldBe(("scheduled", "succeeded", monday.Id));
        var overview = await OverviewAsync();
        overview.Eod.LastRun!.Status.ShouldBe("succeeded");
        (overview.Gaps.Open, overview.Gaps.Filled).ShouldBe((0, 1));
        (await NoticesAsync()).Items.ShouldBeEmpty();

        // Tuesday: Dhan returns only 100 of today's minutes: the run succeeds, today is an open gap, the bell is told.
        day = 13;
        served = 100;
        await TickAtAsync(At(13, 15, 50));
        var tuesday = (await DailyJobsAsync())[0];
        tuesday.Status.ShouldBe("succeeded", tuesday.Error);
        (tuesday.Dhan.GapsFound, tuesday.Dhan.GapsFilled, tuesday.Dhan.GapsUnfillable).ShouldBe((1, 0, 0));
        var gap = (await GapsAsync("?status=open")).Items.ShouldHaveSingleItem();
        (gap.Date, gap.Found, gap.Expected).ShouldBe((new DateOnly(2026, 10, 13), 100, 375));
        var notice = (await NoticesAsync()).Items.ShouldHaveSingleItem();
        (notice.Kind, notice.Text).ShouldBe(("data_gaps_open", "1 trading session has missing price bars"));

        // Wednesday: nothing more exists for Tuesday: the gap is given up (unfillable) and the notice goes away.
        day = 14;
        served = 375;
        await TickAtAsync(At(14, 15, 50));
        (await GapsStatusesAsync()).ShouldBe([("2026-10-06", "filled"), ("2026-10-13", "unfillable")]);
        (await NoticesAsync()).Items.ShouldBeEmpty();
        var last = (await OverviewAsync()).Eod;
        (last.LastRun!.Status, last.LastRun.Trigger).ShouldBe(("succeeded", "daily"));
        source.Plans.Count.ShouldBe(3);
        (await StoredBarsAsync(abb)).Count.ShouldBeGreaterThan(Sessions.Length * SessionClock.RegularBars);
    }

    /// <summary>What a fake Dhan holds: the missing part of the 6th, and the first <paramref name="served"/> minutes of the current day.</summary>
    private static IEnumerable<Bar> Serve(FetchWindow window, int day, int served)
    {
        var from = IndiaDate(window.From);
        var to = IndiaDate(window.To);
        if (from <= Partial && Partial <= to)
        {
            foreach (var bar in BarFixtures.Session(Partial, 1, startMinute: SessionClock.OpenMinute + 200, count: SessionClock.RegularBars - 200))
            {
                yield return bar;
            }
        }

        var today = new DateOnly(2026, 10, day);
        if (from <= today && today <= to)
        {
            foreach (var bar in BarFixtures.Session(today, 1, count: served))
            {
                yield return bar;
            }
        }
    }

    private static DateOnly IndiaDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Ist).DateTime);

    private async Task<GapPage> GapsAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/gaps" + query, Ct);
        return await response.ReadAsync<GapPage>(Ct);
    }

    private async Task<List<(string Date, string Status)>> GapsStatusesAsync() =>
        [.. (await GapsAsync()).Items.OrderBy(g => g.Date).Select(g => (g.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), g.Status))];

    [Fact]
    public async Task Without_a_connected_data_account_the_run_is_recorded_as_skipped_with_the_reason_once_per_day()
    {
        Factory.Dhan.UseAccount(new DataAccountState(null, null, DataAccountStatuses.None, "No account has the primary Data role. Add or choose one under Accounts."));

        await TickAtAsync(At(12, 15, 50));
        await TickAtAsync(At(12, 16, 30));

        var job = (await JobsAsync()).ShouldHaveSingleItem();
        (job.Status, job.Trigger, job.Error, job.FinishedAt).ShouldBe(("skipped", "daily", "No account has the primary Data role. Add or choose one under Accounts.", job.StartedAt));
        job.Dhan.Status.ShouldBe("notRequested");
        var run = (await OverviewAsync()).Eod.LastRun!;
        (run.Status, run.Message, run.JobId).ShouldBe(("skipped", job.Error, job.Id));
        var serviceRun = (await EodRunsAsync()).ShouldHaveSingleItem();
        (serviceRun.Status, serviceRun.Message, serviceRun.Trigger).ShouldBe(("skipped", job.Error, "scheduled"));
        (await OverviewAsync()).LastJobs.Dhan.ShouldBeNull(); // a skipped record is not the last Dhan phase
        (await NoticesAsync()).Items.ShouldBeEmpty();
        (await ScalarAsync<long>("SELECT count(*) FROM market.data_jobs WHERE status = 'running'")).ShouldBe(0);
    }

    [Fact]
    public async Task While_a_job_runs_at_the_time_the_run_is_skipped_and_the_running_job_is_untouched()
    {
        Factory.Dhan.UseConnectedAccount();
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1));
        Factory.Clock.Pin(At(12, 15, 40));
        await ResignInAsync();
        var running = await StartAsync(["aggregate"]); // registered, not run yet: it is "running"

        await TickWithoutRunningJobsAsync(At(12, 15, 50));
        await Runner.WhenIdleAsync();

        var jobs = await JobsAsync();
        jobs.Select(j => (j.Status, j.Trigger)).ShouldBe([("skipped", "daily"), ("running", "manual")]);
        jobs[0].Error.ShouldBe("Another data update was running.");
        (await GetJobAsync(running.Id)).Status.ShouldBe("running");
    }

    [Fact]
    public async Task A_scheduled_run_whose_dhan_phase_crashes_fails_and_raises_the_service_notice_that_names_the_run()
    {
        var id = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        Factory.Dhan.UseSource(_ => throw new InvalidOperationException("Dhan went away"));
        Factory.Dhan.UseConnectedAccount();

        await TickAtAsync(At(12, 15, 50));

        var job = (await JobsAsync()).ShouldHaveSingleItem();
        job.Status.ShouldBe("failed");
        (await NoticesAsync()).Items.ShouldBeEmpty(); // a failed run is no data notice any more: the service's notice below is the only one
        var run = (await OverviewAsync()).Eod.LastRun!;
        (run.Status, run.Trigger).ShouldBe(("failed", "daily"));
        var failedRun = (await EodRunsAsync()).ShouldHaveSingleItem();
        (failedRun.Status, failedRun.Message).ShouldBe(("failed", job.Error));
        var notice = (await ServiceNoticesAsync()).Items.ShouldHaveSingleItem();
        (notice.Kind, notice.Severity, notice.Id).ShouldBe(("service_failed", "error", "service:failed:eod:2026-10-12"));
        notice.Text.ShouldStartWith("End-of-day data run failed on 12 Oct: ");

        // A manual failure is the owner's own business and a later successful scheduled run clears the notice.
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1));
        await TickAtAsync(At(13, 15, 50));
        (await NoticesAsync()).Items.ShouldBeEmpty();
        (await ServiceNoticesAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holiday_or_a_weekend_starts_nothing()
    {
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-12", kind = "holiday" }, Ct)).EnsureSuccessStatusCode();

        await TickAtAsync(At(12, 16, 0));
        await TickAtAsync(At(17, 16, 0)); // Saturday the 17th

        (await JobsAsync()).ShouldBeEmpty();
        var eod = (await OverviewAsync()).Eod;
        (eod.TimeIst, eod.LastRun).ShouldBe(("15:50", null));
        (await EodRunsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_overview_reads_the_schedule_of_the_services_screen_so_an_edit_there_moves_the_next_run()
    {
        var running = (await OverviewAsync()).Eod;
        (running.Enabled, running.TimeIst, running.NextRunAt, running.ScheduleWords).ShouldBe((true, "15:50", At(12, 15, 50), "Every trading day at 15:50 IST, never before the session has ended"));

        using var current = await Client.GetAsync("/api/services/eod", Ct);
        var revision = (await current.ReadAsync<ServiceView>(Ct)).Revision;
        using var saved = await Client.PutJsonAsync("/api/services/eod/schedule", new
        {
            enabled = true,
            schedule = new { type = "daily", time = "16:20", days = WeekDays, tradingDaysOnly = true },
            retries = 0,
            timeoutMinutes = 240,
            catchUp = true,
            notifyOnFailure = true,
            revision,
        }, Ct);
        saved.EnsureSuccessStatusCode();
        var edited = (await OverviewAsync()).Eod;
        (edited.TimeIst, edited.NextRunAt).ShouldBe(("16:20", At(12, 16, 20)));

        using var paused = await Client.PostAsync("/api/services/eod/pause", Ct);
        paused.EnsureSuccessStatusCode();
        var off = (await OverviewAsync()).Eod;
        (off.Enabled, off.TimeIst, off.NextRunAt, off.LastRun).ShouldBe((false, "16:20", null, null));
    }

    [Fact]
    public async Task The_jobs_endpoints_carry_the_trigger_of_every_job()
    {
        var manual = await StartAsync(["aggregate"]);
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);

        manual.Trigger.ShouldBe("manual");
        using var response = await Client.GetAsync($"/api/data/jobs/{manual.Id}", Ct);
        (await response.ReadAsync<DataJobView>(Ct)).Trigger.ShouldBe("manual");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
