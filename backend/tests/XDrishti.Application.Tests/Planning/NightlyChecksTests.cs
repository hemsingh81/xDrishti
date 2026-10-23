using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Planning;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The pure decisions of the gates (spec P7-08): each input read both ways, and a missing input is unavailable, never a pass.</summary>
public sealed class NightlyChecksTests
{
    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static readonly DateTimeOffset Evening = new(2026, 10, 23, 13, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static GateContext Context(DateTimeOffset? now = null, TradingCalendar? calendar = null, DateOnly? session = null, DateOnly? forDate = null) =>
        new(session ?? Friday, forDate ?? Monday, calendar ?? new TradingCalendar(), now ?? Evening);

    private static TradingCalendar Holiday(DateOnly date) =>
        new([TradingDay.Holiday(date, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

    [Fact]
    public void Calendar_passes_after_the_close_with_a_next_session()
    {
        NightlyChecks.Calendar(Context()).Status.ShouldBe(GateStatus.Pass);
    }

    [Fact]
    public void Calendar_fails_before_the_close()
    {
        NightlyChecks.Calendar(Context(now: new DateTimeOffset(2026, 10, 23, 15, 0, 0, Ist))).Status.ShouldBe(GateStatus.Fail);
    }

    [Fact]
    public void Calendar_fails_when_the_session_is_a_holiday_or_weekend()
    {
        NightlyChecks.Calendar(Context(calendar: Holiday(Friday))).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Calendar(Context(session: new DateOnly(2026, 10, 24), forDate: Monday)).Status.ShouldBe(GateStatus.Fail);
    }

    [Fact]
    public void Calendar_fails_when_the_cutoff_of_the_planned_session_has_passed()
    {
        var late = new DateTimeOffset(2026, 10, 26, 9, 6, 0, Ist);

        NightlyChecks.Calendar(Context(now: late)).Status.ShouldBe(GateStatus.Fail);
    }

    [Fact]
    public void Calendar_planning_for_a_date_that_is_not_the_next_session_fails()
    {
        NightlyChecks.Calendar(Context(calendar: Holiday(Monday), forDate: Monday)).Status.ShouldBe(GateStatus.Fail);
    }

    [Fact]
    public void Calendar_reads_unavailable_when_no_session_follows_within_the_search()
    {
        var days = Enumerable.Range(1, NightlyRules.NextSessionSearchDays).Select(i => Friday.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch));

        NightlyChecks.Calendar(Context(calendar: new TradingCalendar(days))).Status.ShouldBe(GateStatus.Unavailable);
    }

    [Fact]
    public void Next_session_skips_the_weekend_and_a_holiday()
    {
        NightlyRules.NextSession(new TradingCalendar(), Friday).ShouldBe(Monday);
        NightlyRules.NextSession(Holiday(Monday), Friday).ShouldBe(new DateOnly(2026, 10, 27));
    }

    private static ServiceRun Run(ServiceRunStatus status, DateTimeOffset startedAt, string? message = null)
    {
        var run = ServiceRun.Start("svc", ServiceRunTrigger.Scheduled, 1, startedAt);
        var end = startedAt.AddMinutes(1);
        switch (status)
        {
            case ServiceRunStatus.Succeeded:
                run.Succeed(end, message);
                break;
            case ServiceRunStatus.Skipped:
                run.Skip(end, message ?? "skipped");
                break;
            default:
                run.Fail(end, message ?? "failed");
                break;
        }

        return run;
    }

    [Fact]
    public void A_service_run_counts_only_after_the_close_and_before_the_planned_day()
    {
        var ok = Run(ServiceRunStatus.Succeeded, new DateTimeOffset(2026, 10, 23, 16, 0, 0, Ist));
        var early = Run(ServiceRunStatus.Succeeded, new DateTimeOffset(2026, 10, 23, 14, 0, 0, Ist));
        var late = Run(ServiceRunStatus.Succeeded, new DateTimeOffset(2026, 10, 26, 7, 0, 0, Ist));
        var failed = Run(ServiceRunStatus.Failed, new DateTimeOffset(2026, 10, 23, 16, 0, 0, Ist));

        NightlyChecks.ServiceRan("eod", "x", [ok], Context(), null).Status.ShouldBe(GateStatus.Pass);
        NightlyChecks.ServiceRan("eod", "x", [early, late, failed], Context(), null).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.ServiceRan("eod", "x", [], Context(), null).Status.ShouldBe(GateStatus.Fail);
    }

    [Fact]
    public void A_skipped_run_passes_only_with_the_accepted_reason()
    {
        var skipped = Run(ServiceRunStatus.Skipped, new DateTimeOffset(2026, 10, 23, 16, 30, 0, Ist), "nothing to sync");

        NightlyChecks.ServiceRan("g", "x", [skipped], Context(), "nothing to sync").Status.ShouldBe(GateStatus.Pass);
        NightlyChecks.ServiceRan("g", "x", [skipped], Context(), "other").Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.ServiceRan("g", "x", [skipped], Context(), null).Status.ShouldBe(GateStatus.Fail);
    }

    private static BarTotals Bars(int instruments, int behind, int empty, DateTimeOffset? last) => new(instruments, 1000, null, last, behind, empty);

    [Fact]
    public void Fresh_data_reads_the_newest_bar_and_the_share_of_instruments_behind()
    {
        var friday = new DateTimeOffset(2026, 10, 23, 15, 29, 0, Ist);

        NightlyChecks.Fresh(Bars(100, 5, 0, friday), Context()).Status.ShouldBe(GateStatus.Pass);
        NightlyChecks.Fresh(Bars(100, 6, 0, friday), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Fresh(Bars(100, 3, 3, friday), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Fresh(Bars(100, 0, 0, friday.AddDays(-1)), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Fresh(Bars(0, 0, 0, null), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Fresh(Bars(0, 0, 0, friday), Context()).Status.ShouldBe(GateStatus.Fail); // an empty universe is no pass
    }

    private static QualityRunView Quality(string status, DateTimeOffset? finished, int instruments = 100, int failed = 0) =>
        new(Guid.NewGuid(), "daily", status, Evening.AddHours(-1), finished, 1, instruments, instruments - failed, 0, failed, 1, null, null, null);

    [Fact]
    public void Quality_needs_a_full_run_finished_after_the_close_with_few_failures()
    {
        var after = new DateTimeOffset(2026, 10, 23, 16, 0, 0, Ist);

        NightlyChecks.Quality(null, Context()).Status.ShouldBe(GateStatus.Unavailable);
        NightlyChecks.Quality(Quality("succeeded", after), Context()).Status.ShouldBe(GateStatus.Pass);
        NightlyChecks.Quality(Quality("succeeded", after.AddDays(-1)), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Quality(Quality("succeeded", null), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Quality(Quality("failed", after), Context()).Status.ShouldBe(GateStatus.Fail);
        NightlyChecks.Quality(Quality("succeeded", after, failed: 6), Context()).Status.ShouldBe(GateStatus.Fail);
    }

    [Fact]
    public void Selection_inputs_need_a_snapshot_not_from_the_future_and_not_stale()
    {
        NightlyChecks.Selection(null, Context()).Status.ShouldBe(GateStatus.Unavailable);
        NightlyChecks.Selection(Friday, Context()).Status.ShouldBe(GateStatus.Pass);
        NightlyChecks.Selection(Friday.AddDays(-7), Context()).Status.ShouldBe(GateStatus.Pass); // 5 sessions
        NightlyChecks.Selection(Friday.AddDays(-8), Context()).Status.ShouldBe(GateStatus.Fail); // 6 sessions
        NightlyChecks.Selection(Monday, Context()).Status.ShouldBe(GateStatus.Fail); // look-ahead
    }

    [Fact]
    public void A_reason_is_clipped_to_the_column()
    {
        GateResult.Failed("g", new string('x', 900)).Reason.Length.ShouldBe(GateResult.MaxReasonLength);
    }
}
