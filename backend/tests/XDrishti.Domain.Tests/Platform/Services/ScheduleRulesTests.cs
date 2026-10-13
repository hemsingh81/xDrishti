using System.Text.Json;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Domain.Tests.Platform.Services.ServiceTestData;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>
/// The checks over the whole set of services (ADR 0018 §6): every error that blocks a save and every warning that does not, the effect of one service's change on the others, and the rule that only
/// registered services are evaluated (pre-open, nightly and backup are test definitions here: those services do not exist yet).
/// </summary>
public sealed class ScheduleRulesTests
{
    private static readonly ServiceConstraints EodRules = new() { StartNotBefore = T(15, 35), StartAdvice = "The session ends at 15:30.", NotBeforeSessionEnd = true };

    private static RuleSubject Eod(Schedule? schedule = null, bool enabled = true) => Subject("eod", schedule ?? Schedule.Daily(T(15, 50)), EodRules, enabled);

    private static RuleSubject PreOpen(Schedule? schedule = null) =>
        Subject("pre-open", schedule ?? Schedule.Daily(T(9, 10)), new ServiceConstraints { StartNotBefore = T(9, 8), StartNotAfter = T(9, 15), StartAdvice = "Run it after the review cutoff and before the open." });

    private static RuleSubject Nightly(Schedule? schedule = null) =>
        Subject("nightly", schedule ?? Schedule.Daily(T(18, 30)), new ServiceConstraints { MustFollow = ["eod", "portfolio-sync"] });

    private static RuleSubject Backup(Schedule? schedule = null) =>
        Subject("backup", schedule ?? Schedule.Daily(T(23, 30), false, Weekdays.EveryDay), new ServiceConstraints { MustNotOverlap = TimeRange.MarketHours });

    private static RuleSubject Feed(Schedule? schedule = null) =>
        Subject("feed", schedule ?? Schedule.Window(T(9, 0), T(15, 35)), new ServiceConstraints { MustCover = TimeRange.MarketHours });

    private static List<ScheduleIssue> Evaluate(params RuleSubject[] set) => [.. ScheduleRules.Evaluate(set)];

    private static ScheduleIssue Only(IEnumerable<ScheduleIssue> issues, string code) => issues.Where(i => i.Code == code).ShouldHaveSingleItem();

    // ---- a clean set ----

    [Fact]
    public void The_defaults_of_the_services_are_free_of_findings()
    {
        Evaluate(Eod(), PreOpen(), Nightly(), Backup(), Feed()).ShouldBeEmpty();
    }

    // ---- errors ----

    [Fact]
    public void A_schedule_type_the_service_does_not_allow_is_an_error_on_the_type()
    {
        var subject = Subject("eod", Schedule.Continuous(), EodRules, allowed: [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After]);

        var issue = Only(Evaluate(subject), ScheduleIssueCodes.TypeNotAllowed);

        (issue.ServiceId, issue.Field, issue.Severity).ShouldBe(("eod", "schedule.type", IssueSeverity.Error));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void Retries_must_be_from_0_to_5(int retries, bool error)
    {
        var subject = new RuleSubject(Definition("eod", Schedule.Daily(T(15, 50))), Settings(Schedule.Daily(T(15, 50)), retries: retries));

        Evaluate(subject).Any(i => i is { Field: "retries", Severity: IssueSeverity.Error }).ShouldBe(error);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(240, false)]
    [InlineData(241, true)]
    public void The_timeout_must_be_from_1_to_240_minutes(int timeout, bool error)
    {
        var subject = new RuleSubject(Definition("eod", Schedule.Daily(T(15, 50))), Settings(Schedule.Daily(T(15, 50)), timeout: timeout));

        Evaluate(subject).Any(i => i is { Field: "timeoutMinutes", Severity: IssueSeverity.Error }).ShouldBe(error);
    }

    [Theory]
    [InlineData(9, 0, 9, 0)] // start equal to end
    [InlineData(10, 0, 9, 0)] // start after end
    public void A_window_must_start_before_it_ends(int fromHour, int fromMinute, int toHour, int toMinute)
    {
        var window = Schedule.Window(T(fromHour, fromMinute), T(toHour, toMinute));

        var issue = Only(Evaluate(Subject("feed", window)), ScheduleIssueCodes.WindowOrder);

        (issue.Field, issue.Severity).ShouldBe(("schedule.to", IssueSeverity.Error));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public void An_interval_is_from_1_to_120_minutes(int every, bool error)
    {
        var interval = Schedule.Interval(every, T(0, 0), T(23, 59));

        Evaluate(Subject("alerts", interval)).Any(i => i is { Code: ScheduleIssueCodes.IntervalRange, Field: "schedule.everyMinutes" }).ShouldBe(error);
    }

    [Fact]
    public void An_interval_longer_than_its_window_is_an_error()
    {
        var interval = Schedule.Interval(61, T(9, 0), T(10, 0));

        var issue = Only(Evaluate(Subject("alerts", interval)), ScheduleIssueCodes.IntervalExceedsWindow);

        (issue.Field, issue.Severity).ShouldBe(("schedule.everyMinutes", IssueSeverity.Error));
        Evaluate(Subject("alerts", Schedule.Interval(60, T(9, 0), T(10, 0)))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ScheduleType.Daily)]
    [InlineData(ScheduleType.Window)]
    [InlineData(ScheduleType.Interval)]
    public void No_day_chosen_is_an_error_unless_trading_days_decide(ScheduleType type)
    {
        var none = type switch
        {
            ScheduleType.Daily => new Schedule(type, Time: T(10, 0)),
            ScheduleType.Window => new Schedule(type, From: T(9, 0), To: T(10, 0)),
            _ => new Schedule(type, From: T(9, 0), To: T(10, 0), EveryMinutes: 5),
        };

        Only(Evaluate(Subject("job", none)), ScheduleIssueCodes.NoDay).Field.ShouldBe("schedule.days");
        Evaluate(Subject("job", none with { TradingDaysOnly = true })).ShouldBeEmpty();
        Evaluate(Subject("job", none with { Days = Weekdays.Saturday })).ShouldBeEmpty();
    }

    [Fact]
    public void A_service_that_runs_after_itself_is_circular()
    {
        var issue = Only(Evaluate(Subject("a", Schedule.AfterService("a"))), ScheduleIssueCodes.ChainCircular);

        (issue.ServiceId, issue.Field, issue.Severity).ShouldBe(("a", "schedule.after", IssueSeverity.Error));
    }

    [Fact]
    public void A_circle_of_run_after_links_is_an_error_on_every_member_and_not_on_a_bystander()
    {
        var circle = Evaluate(
            Subject("a", Schedule.AfterService("c")),
            Subject("b", Schedule.AfterService("a")),
            Subject("c", Schedule.AfterService("b")),
            Subject("d", Schedule.AfterService("a")));

        circle.Where(i => i.Code == ScheduleIssueCodes.ChainCircular).Select(i => i.ServiceId).Order().ShouldBe(["a", "b", "c"]);
        circle.ShouldAllBe(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Running_after_an_always_on_service_is_an_error()
    {
        var issue = Only(Evaluate(Subject("feed", Schedule.Continuous()), Subject("report", Schedule.AfterService("feed"))), ScheduleIssueCodes.ChainTargetContinuous);

        (issue.ServiceId, issue.Field, issue.RelatedServiceId).ShouldBe(("report", "schedule.after", "feed"));
    }

    [Fact]
    public void Running_after_a_service_that_is_not_registered_is_an_error()
    {
        var issue = Only(Evaluate(Subject("report", Schedule.AfterService("nobody"))), ScheduleIssueCodes.AfterUnknown);

        issue.Field.ShouldBe("schedule.after");
    }

    [Fact]
    public void A_run_after_schedule_without_a_service_is_an_error()
    {
        Only(Evaluate(Subject("report", new Schedule(ScheduleType.After))), ScheduleIssueCodes.AfterMissing).Field.ShouldBe("schedule.after");
    }

    [Fact]
    public void A_parameter_the_service_does_not_have_and_one_outside_its_bounds_are_errors()
    {
        var parameters = new[] { ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5), ParameterDefinition.Fractional("multiple", "Spike multiple", 2, 100, 20), ParameterDefinition.Switch("strict", "Strict", false) };
        var definition = Definition("eod", Schedule.Daily(T(15, 50)), parameters: parameters);
        var given = new Dictionary<string, JsonElement>
        {
            ["lookback"] = JsonSerializer.SerializeToElement(31),
            ["multiple"] = JsonSerializer.SerializeToElement(1.5),
            ["strict"] = JsonSerializer.SerializeToElement("yes"),
            ["mystery"] = JsonSerializer.SerializeToElement(1),
        };

        var issues = Evaluate(new RuleSubject(definition, Settings(Schedule.Daily(T(15, 50))) with { Parameters = given }));

        issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => (i.Field, i.Code)).Order().ShouldBe(
        [
            ("parameters.lookback", ScheduleIssueCodes.ParameterInvalid),
            ("parameters.multiple", ScheduleIssueCodes.ParameterInvalid),
            ("parameters.mystery", ScheduleIssueCodes.ParameterUnknown),
            ("parameters.strict", ScheduleIssueCodes.ParameterInvalid),
        ]);
    }

    [Fact]
    public void A_long_or_odd_parameter_name_from_the_request_is_shortened_when_it_is_repeated_in_the_answer()
    {
        var name = "x\n" + new string('y', 200);
        var given = new Dictionary<string, JsonElement> { [name] = JsonSerializer.SerializeToElement(1) };

        var issue = Evaluate(new RuleSubject(Definition("eod", Schedule.Daily(T(15, 50))), Settings(Schedule.Daily(T(15, 50))) with { Parameters = given })).ShouldHaveSingleItem();

        issue.Field.Length.ShouldBeLessThan(70);
        (issue.Field + issue.Message).ShouldNotContain('\n');
    }

    [Fact]
    public void Parameters_inside_their_bounds_are_accepted()
    {
        var parameters = new[] { ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5), ParameterDefinition.Switch("strict", "Strict", false) };
        var definition = Definition("eod", Schedule.Daily(T(15, 50)), parameters: parameters);
        var given = new Dictionary<string, JsonElement> { ["lookback"] = JsonSerializer.SerializeToElement(30), ["strict"] = JsonSerializer.SerializeToElement(true) };

        Evaluate(new RuleSubject(definition, Settings(Schedule.Daily(T(15, 50))) with { Parameters = given })).ShouldBeEmpty();
    }

    // ---- warnings ----

    [Theory]
    [InlineData(15, 20, true)]
    [InlineData(15, 34, true)]
    [InlineData(15, 35, false)]
    [InlineData(15, 50, false)]
    public void An_end_of_day_fetch_before_15_35_is_warned_about(int hour, int minute, bool warned)
    {
        var issues = Evaluate(Eod(Schedule.Daily(T(hour, minute))));

        issues.Any(i => i is { Code: ScheduleIssueCodes.StartTooEarly, Severity: IssueSeverity.Warning, ServiceId: "eod", Field: "schedule.time" }).ShouldBe(warned);
        issues.Any(i => i.Severity == IssueSeverity.Error).ShouldBeFalse();
    }

    [Fact]
    public void A_pre_open_check_is_warned_about_before_09_08_and_after_09_15_but_not_between()
    {
        Evaluate(PreOpen(Schedule.Daily(T(9, 5)))).ShouldHaveSingleItem().Code.ShouldBe(ScheduleIssueCodes.StartTooEarly);
        Evaluate(PreOpen(Schedule.Daily(T(9, 20)))).ShouldHaveSingleItem().Code.ShouldBe(ScheduleIssueCodes.StartTooLate);
        Evaluate(PreOpen(Schedule.Daily(T(9, 8)))).ShouldBeEmpty();
        Evaluate(PreOpen(Schedule.Daily(T(9, 15)))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(9, 30, 15, 30, true)] // starts after the open
    [InlineData(9, 0, 15, 0, true)] // ends before the close
    [InlineData(9, 15, 15, 30, false)]
    [InlineData(9, 0, 15, 35, false)]
    public void A_window_that_does_not_cover_the_session_is_warned_about_when_the_definition_requires_it(int fh, int fm, int th, int tm, bool warned)
    {
        var issues = Evaluate(Feed(Schedule.Window(T(fh, fm), T(th, tm))));

        issues.Any(i => i is { Code: ScheduleIssueCodes.WindowNotCovering, Severity: IssueSeverity.Warning }).ShouldBe(warned);
    }

    [Fact]
    public void A_window_that_does_not_have_to_cover_the_session_is_not_warned_about()
    {
        Evaluate(Subject("other", Schedule.Window(T(10, 0), T(11, 0)))).ShouldBeEmpty();
    }

    [Fact]
    public void An_always_on_service_covers_the_session()
    {
        Evaluate(Feed(Schedule.Continuous())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(11, 0, true)]
    [InlineData(9, 15, true)]
    [InlineData(15, 30, true)]
    [InlineData(15, 31, false)]
    [InlineData(9, 14, false)]
    public void A_backup_inside_market_hours_on_a_trading_day_is_warned_about(int hour, int minute, bool warned)
    {
        var issues = Evaluate(Backup(Schedule.Daily(T(hour, minute), false, Weekdays.EveryDay)));

        issues.Any(i => i is { Code: ScheduleIssueCodes.OverlapsMarketHours, Severity: IssueSeverity.Warning, Field: "schedule.time" }).ShouldBe(warned);
    }

    [Fact]
    public void A_backup_inside_market_hours_on_weekends_only_is_not_warned_about()
    {
        Evaluate(Backup(Schedule.Daily(T(11, 0), false, Weekdays.Saturday | Weekdays.Sunday))).ShouldBeEmpty();
    }

    [Fact]
    public void A_backup_window_that_touches_market_hours_is_warned_about()
    {
        Evaluate(Backup(Schedule.Window(T(14, 0), T(16, 0), false, Weekdays.MondayToFriday))).ShouldHaveSingleItem().Code.ShouldBe(ScheduleIssueCodes.OverlapsMarketHours);
        Evaluate(Backup(Schedule.Window(T(16, 0), T(17, 0), false, Weekdays.MondayToFriday))).ShouldBeEmpty();
    }

    [Fact]
    public void A_chain_of_more_than_three_services_is_warned_about_on_its_last_service()
    {
        var chain = Evaluate(
            Subject("a", Schedule.Daily(T(10, 0))),
            Subject("b", Schedule.AfterService("a")),
            Subject("c", Schedule.AfterService("b")));
        chain.ShouldBeEmpty(); // three services are fine

        var longer = Evaluate(
            Subject("a", Schedule.Daily(T(10, 0))),
            Subject("b", Schedule.AfterService("a")),
            Subject("c", Schedule.AfterService("b")),
            Subject("d", Schedule.AfterService("c")));

        var issue = longer.ShouldHaveSingleItem();
        (issue.ServiceId, issue.Code, issue.Severity).ShouldBe(("d", ScheduleIssueCodes.ChainLong, IssueSeverity.Warning));
    }

    [Fact]
    public void Two_services_at_the_same_minute_on_the_same_day_are_each_warned_about()
    {
        var issues = Evaluate(Subject("a", Schedule.Daily(T(10, 0))), Subject("b", Schedule.Daily(T(10, 0))));

        issues.Select(i => (i.ServiceId, i.RelatedServiceId, i.Code)).ShouldBe([("a", "b", ScheduleIssueCodes.SameMinute), ("b", "a", ScheduleIssueCodes.SameMinute)]);
    }

    [Fact]
    public void Services_at_the_same_minute_on_different_days_or_with_one_paused_are_not_warned_about()
    {
        Evaluate(Subject("a", Schedule.Daily(T(10, 0))), Subject("b", Schedule.Weekly(DayOfWeek.Saturday, T(10, 0)))).ShouldBeEmpty();
        Evaluate(Subject("a", Schedule.Daily(T(10, 0))), Subject("b", Schedule.Daily(T(10, 0)), enabled: false)).ShouldBeEmpty();
        Evaluate(Subject("a", Schedule.Daily(T(10, 0))), Subject("b", Schedule.Daily(T(10, 1)))).ShouldBeEmpty();
    }

    [Fact]
    public void A_paused_service_gets_no_warnings_but_still_its_errors()
    {
        var paused = Subject("eod", Schedule.Daily(T(15, 0)), EodRules, enabled: false);
        Evaluate(paused).ShouldBeEmpty();

        var broken = Subject("feed", Schedule.Window(T(10, 0), T(9, 0)), enabled: false);
        Evaluate(broken).ShouldHaveSingleItem().Code.ShouldBe(ScheduleIssueCodes.WindowOrder);
    }

    // ---- the whole set ----

    [Fact]
    public void Rules_of_services_that_are_not_registered_are_not_evaluated()
    {
        // only the end-of-day run is registered: no pre-open, nightly or backup constraint can show up, and the nightly's link to the portfolio sync does not exist
        Evaluate(Eod()).ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_predecessor_is_skipped_but_a_registered_one_is_checked()
    {
        // nightly must follow eod and portfolio-sync; only eod is registered
        var onlyEod = Evaluate(Eod(), Nightly(Schedule.Daily(T(15, 20))));

        var issue = Only(onlyEod, ScheduleIssueCodes.MustFollow);

        (issue.ServiceId, issue.RelatedServiceId).ShouldBe(("nightly", "eod"));
    }

    [Fact]
    public void Saving_the_end_of_day_time_to_15_20_warns_on_the_end_of_day_run()
    {
        var issues = Evaluate(Eod(Schedule.Daily(T(15, 20))), Nightly());

        issues.ShouldHaveSingleItem().ShouldBe(new ScheduleIssue("eod", "schedule.time", IssueSeverity.Warning, ScheduleIssueCodes.StartTooEarly, issues[0].Message));
    }

    [Fact]
    public void Moving_the_end_of_day_run_after_a_dependent_services_own_time_is_reported_on_the_dependent()
    {
        var issues = Evaluate(Eod(Schedule.Daily(T(19, 0))), Nightly(Schedule.Daily(T(18, 30))));

        var issue = issues.ShouldHaveSingleItem();
        (issue.ServiceId, issue.Code, issue.RelatedServiceId, issue.Severity).ShouldBe(("nightly", ScheduleIssueCodes.MustFollow, "eod", IssueSeverity.Warning));
    }

    [Fact]
    public void A_dependent_that_runs_after_its_predecessor_in_the_chain_follows_it_whatever_the_time()
    {
        Evaluate(Eod(), Nightly(Schedule.AfterService("eod"))).ShouldBeEmpty();
    }

    [Fact]
    public void A_dependent_that_runs_after_something_else_is_warned_about()
    {
        var issues = Evaluate(Eod(), Subject("other", Schedule.Daily(T(10, 0))), Nightly(Schedule.AfterService("other")));

        Only(issues, ScheduleIssueCodes.MustFollow).ServiceId.ShouldBe("nightly");
    }

    [Fact]
    public void A_paused_predecessor_is_not_waited_for()
    {
        Evaluate(Eod(enabled: false), Nightly(Schedule.Daily(T(15, 20)))).ShouldBeEmpty();
    }

    [Fact]
    public void Errors_come_before_warnings()
    {
        var issues = Evaluate(Eod(Schedule.Daily(T(15, 0))), Subject("feed", Schedule.Window(T(10, 0), T(9, 0))));

        issues.Select(i => i.Severity).ShouldBe([IssueSeverity.Error, IssueSeverity.Warning]);
    }
}
