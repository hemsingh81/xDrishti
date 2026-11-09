using System.Diagnostics;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Domain.Tests.Platform.Services.ServiceTestData;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>
/// The recovery planner (ADR 0240, spec AU-01), by hand tables. October 2026: Mon 5, Tue 6, Wed 7, Thu 8, Fri 9, Sat 10, Sun 11, Mon 12. All instants India time unless given.
/// </summary>
public sealed class RecoveryPlannerTests
{
    private static readonly TradingCalendar Plain = new();

    private static readonly Schedule EndOfDay = Schedule.Daily(T(15, 50));

    private static readonly ServiceConstraints AfterClose = new() { NotBeforeSessionEnd = true };

    private static ServiceConstraints Class(RecoveryClass cls, params string[] follow) => new() { RecoveryClass = cls, MustFollow = follow, NotBeforeSessionEnd = true };

    private static RecoveryInput Input(string id, RecoveryClass cls, DateTimeOffset? last, Schedule? schedule = null, bool enabled = true, bool catchUp = true, params string[] follow)
    {
        var s = schedule ?? EndOfDay;
        var settings = Settings(s, enabled) with { CatchUp = catchUp };
        return new RecoveryInput(Definition(id, s, Class(cls, follow)), settings, last);
    }

    private static RecoveryPlan Plan(IReadOnlyList<RecoveryInput> services, DateTimeOffset now, TradingCalendar? calendar = null, int max = 30)
    {
        var result = RecoveryPlanner.Plan(services, calendar ?? Plain, now, max);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private static DateOnly[] Sessions(RecoveryPlan plan, string id, RecoveryItemStatus status) =>
        plan.Items.Where(i => i.ServiceId == id && i.Status == status).Select(i => i.Session).Order().ToArray();

    // Last run Mon 5 16:00; now Mon 12 16:00 (session closed): missed Tue 6, Wed 7, Thu 8, Fri 9 and Mon 12, not the weekend.
    private static readonly DateTimeOffset LastRun = At(10, 5, 16, 0);

    private static readonly DateTimeOffset Now = At(10, 12, 16, 0);

    private static readonly DateOnly[] Five = [Date(10, 6), Date(10, 7), Date(10, 8), Date(10, 9), Date(10, 12)];

    [Fact]
    public void A_daily_service_with_no_run_for_five_sessions_lists_five_missed_and_no_weekend()
    {
        var plan = Plan([Input("daily-dated", RecoveryClass.Dated, LastRun)], Now);

        Sessions(plan, "daily-dated", RecoveryItemStatus.Planned).ShouldBe(Five);
        plan.Items.Count.ShouldBe(5);
    }

    [Fact]
    public void A_holiday_is_not_missed()
    {
        var plan = Plan([Input("daily-dated", RecoveryClass.Dated, LastRun)], Now, new TradingCalendar([Holiday(10, 8)]));

        Sessions(plan, "daily-dated", RecoveryItemStatus.Planned).ShouldBe([Date(10, 6), Date(10, 7), Date(10, 9), Date(10, 12)]);
    }

    [Fact]
    public void A_special_session_on_a_saturday_is_missed_after_its_own_end()
    {
        var calendar = new TradingCalendar([Special(10, 10, 555, 615)]); // Sat 10th 09:15 to 10:15
        var before = Plan([Input("daily-dated", RecoveryClass.Dated, At(10, 9, 16, 0))], At(10, 10, 15, 45), calendar);
        var after = Plan([Input("daily-dated", RecoveryClass.Dated, At(10, 9, 16, 0))], At(10, 10, 16, 0), calendar);

        before.Items.ShouldBeEmpty(); // due at 15:50 (the configured time is after the special session's end)
        Sessions(after, "daily-dated", RecoveryItemStatus.Planned).ShouldBe([Date(10, 10)]);
    }

    [Fact]
    public void A_service_that_never_ran_is_missed_for_every_session_of_the_reach_only()
    {
        var plan = Plan([Input("never", RecoveryClass.Dated, null)], Now, max: 3);

        Sessions(plan, "never", RecoveryItemStatus.Planned).ShouldBe([Date(10, 8), Date(10, 9), Date(10, 12)]);
        plan.Items.ShouldAllBe(i => i.Status == RecoveryItemStatus.Planned);
    }

    [Fact]
    public void A_convergent_service_has_one_step_over_the_session_range()
    {
        var plan = Plan([Input("eod", RecoveryClass.Convergent, LastRun)], Now);

        var step = plan.Steps.ShouldHaveSingleItem();
        step.ServiceId.ShouldBe("eod");
        step.Sessions.ShouldBe(Five);
        (step.From, step.To).ShouldBe((Date(10, 6), Date(10, 12)));
        Sessions(plan, "eod", RecoveryItemStatus.Planned).ShouldBe(Five);
    }

    [Fact]
    public void A_latest_only_service_runs_the_latest_session_and_supersedes_the_older()
    {
        var plan = Plan([Input("digest", RecoveryClass.LatestOnly, LastRun)], Now);

        plan.Steps.ShouldHaveSingleItem().Sessions.ShouldBe([Date(10, 12)]);
        Sessions(plan, "digest", RecoveryItemStatus.Superseded).ShouldBe(Five[..4]);
        Sessions(plan, "digest", RecoveryItemStatus.Planned).ShouldBe([Date(10, 12)]);
    }

    [Fact]
    public void A_dated_service_has_one_step_per_session_oldest_first()
    {
        var plan = Plan([Input("dated", RecoveryClass.Dated, LastRun)], Now);

        plan.Steps.Select(s => s.Sessions.ShouldHaveSingleItem()).ShouldBe(Five);
    }

    [Fact]
    public void A_time_bound_service_is_not_recoverable_with_its_reason_and_has_no_step()
    {
        var plan = Plan([Input("preopen", RecoveryClass.TimeBound, LastRun)], Now);

        plan.Steps.ShouldBeEmpty();
        plan.Items.Count.ShouldBe(5);
        plan.Items.ShouldAllBe(i => i.Status == RecoveryItemStatus.NotRecoverable && i.Reason == RecoveryPlanner.LiveObservationReason);
        RecoveryItemStatusCodes.Code(RecoveryItemStatus.NotRecoverable).ShouldBe("not_recoverable");
    }

    [Fact]
    public void Steps_follow_data_then_learning_then_nightly_by_the_dependency_chains()
    {
        // Registered in the worst order. nightly follows eod; grade follows eod (learning); the plan service follows nightly by an 'after' schedule; weekly-ish roots are data.
        var services = new[]
        {
            Input("suggestions", RecoveryClass.LatestOnly, LastRun, Schedule.AfterService("nightly")),
            Input("nightly", RecoveryClass.LatestOnly, LastRun, EndOfDay, follow: "eod"),
            Input("shadow-grade", RecoveryClass.Convergent, LastRun, EndOfDay, follow: "eod"),
            Input("history-eod", RecoveryClass.Convergent, LastRun),
            Input("eod", RecoveryClass.Convergent, LastRun),
            Input("decay-monitor", RecoveryClass.LatestOnly, LastRun, EndOfDay, follow: "shadow-grade"),
        };

        var plan = Plan(services, At(10, 12, 16, 0).AddHours(-8)); // Mon 08:00: Monday's cutoff (09:05) is still ahead

        plan.Steps.Select(s => s.ServiceId).ShouldBe(["eod", "history-eod", "shadow-grade", "decay-monitor", "nightly", "suggestions"]);
    }

    [Fact]
    public void A_cycle_is_an_error_not_a_guess()
    {
        var services = new[]
        {
            Input("a", RecoveryClass.Convergent, LastRun, EndOfDay, follow: "b"),
            Input("b", RecoveryClass.Convergent, LastRun, Schedule.AfterService("a")),
            Input("c", RecoveryClass.Convergent, LastRun),
        };

        var result = RecoveryPlanner.Plan(services, Plain, Now, 30);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("recovery.cycle");
        result.Error.Message.ShouldEndWith("cycle: a, b.");
    }

    [Fact]
    public void A_service_that_follows_itself_is_a_cycle()
    {
        RecoveryPlanner.Plan([Input("a", RecoveryClass.Convergent, LastRun, EndOfDay, follow: "a")], Plain, Now, 30).Error.Code.ShouldBe("recovery.cycle");
    }

    [Theory]
    [InlineData(8, 0, true)] // Mon 08:00, last closed Fri 9th, plan for Mon 12th, cutoff 09:05 ahead
    [InlineData(9, 4, true)]
    [InlineData(9, 5, false)] // at the cutoff it is no longer ahead
    [InlineData(10, 0, false)]
    public void The_nightly_step_exists_only_while_the_review_cutoff_is_ahead(int hour, int minute, bool exists)
    {
        var plan = Plan([Input("nightly", RecoveryClass.LatestOnly, At(10, 7, 16, 30))], At(10, 12, hour, minute));

        plan.Steps.Any(s => s.ServiceId == "nightly").ShouldBe(exists);
        if (!exists)
        {
            plan.Items.ShouldNotBeEmpty();
            plan.Items.ShouldAllBe(i => i.Status == RecoveryItemStatus.NotRecoverable && i.Reason == "cutoff passed");
        }
    }

    [Fact]
    public void The_nightly_step_after_a_session_closed_looks_to_the_next_session_across_the_weekend()
    {
        // Fri 9th 16:00: last closed Fri, next session Mon 12th, cutoff Mon 09:05 is ahead.
        var plan = Plan([Input("nightly", RecoveryClass.LatestOnly, At(10, 8, 16, 30))], At(10, 9, 16, 0));

        plan.Steps.ShouldHaveSingleItem().Sessions.ShouldBe([Date(10, 9)]);
    }

    [Fact]
    public void Nightly_with_no_next_session_in_the_calendar_is_not_recoverable()
    {
        var holidays = Enumerable.Range(12, 12).Select(d => Holiday(10, d)).Concat(Enumerable.Range(1, 3).Select(d => Holiday(11, d))).Concat([Holiday(10, 26), Holiday(10, 27)]);
        var calendar = new TradingCalendar(holidays.DistinctBy(h => h.Id)); // Oct 12 to Nov 3: no trading day within 10 days of Fri 9th

        var plan = Plan([Input("nightly", RecoveryClass.LatestOnly, At(10, 8, 16, 30))], At(10, 9, 16, 0), calendar);

        plan.Steps.ShouldBeEmpty();
        plan.Items.ShouldAllBe(i => i.Status == RecoveryItemStatus.NotRecoverable);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_paused_service_or_one_with_catch_up_off_is_not_missed(bool enabled, bool catchUp)
    {
        var plan = Plan([Input("x", RecoveryClass.Convergent, LastRun, enabled: enabled, catchUp: catchUp)], Now);

        plan.Steps.ShouldBeEmpty();
        plan.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Sessions_beyond_the_reach_are_reported_out_of_reach_and_never_planned()
    {
        // Reach 3 sessions from Mon 12th: Mon 12, Fri 9, Thu 8. Tue 6 and Wed 7 are out of reach.
        var plan = Plan([Input("dated", RecoveryClass.Dated, LastRun)], Now, max: 3);

        Sessions(plan, "dated", RecoveryItemStatus.OutOfReach).ShouldBe([Date(10, 6), Date(10, 7)]);
        plan.Items.Where(i => i.Status == RecoveryItemStatus.OutOfReach).ShouldAllBe(i => i.Reason == RecoveryPlanner.OutOfReachReason);
        plan.Steps.Select(s => s.From).ShouldBe([Date(10, 8), Date(10, 9), Date(10, 12)]);
    }

    [Fact]
    public void The_reach_counts_trading_sessions_not_calendar_days()
    {
        // Holiday Thu 8th: the 3-session reach is Mon 12, Fri 9, Wed 7.
        var plan = Plan([Input("dated", RecoveryClass.Dated, LastRun)], Now, new TradingCalendar([Holiday(10, 8)]), max: 3);

        plan.Steps.Select(s => s.From).ShouldBe([Date(10, 7), Date(10, 9), Date(10, 12)]);
        Sessions(plan, "dated", RecoveryItemStatus.OutOfReach).ShouldBe([Date(10, 6)]);
    }

    [Fact]
    public void Just_after_midnight_india_time_the_new_day_is_not_yet_due_and_yesterday_is()
    {
        var now = new DateTimeOffset(2026, 10, 12, 18, 45, 0, TimeSpan.Zero); // 00:15 IST Tue 13th
        var plan = Plan([Input("dated", RecoveryClass.Dated, At(10, 9, 16, 0))], now);

        Sessions(plan, "dated", RecoveryItemStatus.Planned).ShouldBe([Date(10, 12)]);
    }

    [Fact]
    public void A_run_started_at_the_due_time_or_later_counts_as_run()
    {
        var plan = Plan([Input("dated", RecoveryClass.Dated, At(10, 12, 15, 50))], Now); // started exactly at Monday's occurrence

        plan.Items.ShouldBeEmpty();
    }

    [Fact]
    public void A_weekly_service_is_missed_per_week()
    {
        var weekly = Schedule.Weekly(DayOfWeek.Saturday, T(10, 0));
        var plan = Plan([Input("weekly", RecoveryClass.LatestOnly, At(9, 19, 10, 5), weekly)], Now);

        plan.Steps.ShouldHaveSingleItem().Sessions.ShouldBe([Date(10, 10)]);
        Sessions(plan, "weekly", RecoveryItemStatus.Superseded).ShouldBe([Date(9, 26), Date(10, 3)]);
    }

    [Fact]
    public void A_service_with_a_window_is_missed_per_day_and_one_that_follows_an_unregistered_service_is_not_missed()
    {
        var recorder = Schedule.Window(T(9, 15), T(15, 30));
        var plan = Plan(
            [Input("recorder", RecoveryClass.TimeBound, At(10, 8, 12, 0), recorder), Input("orphan", RecoveryClass.Convergent, null, Schedule.AfterService("gone"))],
            Now);

        Sessions(plan, "recorder", RecoveryItemStatus.NotRecoverable).ShouldBe([Date(10, 9), Date(10, 12)]);
        plan.Items.ShouldAllBe(i => i.ServiceId == "recorder");
    }

    [Fact]
    public void A_service_without_a_declared_class_and_a_bad_reach_are_refused()
    {
        var undeclared = new RecoveryInput(Definition("x", EndOfDay), Settings(EndOfDay), null);

        RecoveryPlanner.Plan([undeclared], Plain, Now, 30).Error.Code.ShouldBe("recovery.unclassified");
        RecoveryPlanner.Plan([], Plain, Now, 0).Error.Code.ShouldBe("recovery.reach_invalid");
        RecoveryPlanner.Plan([], Plain, Now, 251).Error.Code.ShouldBe("recovery.reach_invalid");
        RecoveryPlanner.Plan([], Plain, Now, 250).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Forty_services_over_two_hundred_and_fifty_sessions_plan_in_under_fifty_milliseconds()
    {
        var services = Enumerable.Range(0, 40).Select(i => Input($"s{i:00}", (RecoveryClass)(i % 4), null, EndOfDay, follow: i == 0 ? [] : [$"s{i - 1:00}"])).ToArray();
        var now = At(10, 12, 16, 0);
        RecoveryPlanner.Plan(services, Plain, now, 250).IsSuccess.ShouldBeTrue(); // warm-up (JIT)

        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var clock = Stopwatch.StartNew();
            var result = RecoveryPlanner.Plan(services, Plain, now, 250);
            best = clock.Elapsed < best ? clock.Elapsed : best;
            result.Value.Items.Count.ShouldBeGreaterThan(5000);
        }

        best.ShouldBeLessThan(TimeSpan.FromMilliseconds(50));
    }
}
