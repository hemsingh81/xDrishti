using XDrishti.Domain.Platform.Services;
using static XDrishti.Domain.Tests.Platform.Services.ServiceTestData;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>Reading the text form of a schedule (what the API sends and the database stores) into the checked form, and back.</summary>
public sealed class ScheduleSpecTests
{
    private static Schedule? Parse(ScheduleSpec spec, out List<ScheduleIssue> issues)
    {
        issues = [];
        return spec.Parse("svc", issues);
    }

    public static TheoryData<Schedule> Schedules => new()
    {
        Schedule.Daily(T(15, 50)),
        Schedule.Daily(T(8, 45), false, Weekdays.EveryDay),
        Schedule.Daily(T(23, 30), false, Weekdays.Monday | Weekdays.Thursday),
        Schedule.Weekly(DayOfWeek.Saturday, T(10, 0)),
        Schedule.Weekly(DayOfWeek.Monday, T(9, 0), tradingDaysOnly: true),
        Schedule.AfterService("eod"),
        Schedule.Window(T(9, 0), T(15, 35)),
        Schedule.Interval(5, T(9, 15), T(15, 30), false, Weekdays.MondayToFriday | Weekdays.Saturday),
        Schedule.Continuous(),
    };

    [Theory]
    [MemberData(nameof(Schedules))]
    public void Every_schedule_survives_the_round_trip_through_its_text_form(Schedule schedule)
    {
        var parsed = Parse(ScheduleSpec.Of(schedule), out var issues);

        issues.ShouldBeEmpty();
        parsed.ShouldBe(schedule);
    }

    [Fact]
    public void The_text_form_uses_the_codes_of_the_wire_format()
    {
        var spec = ScheduleSpec.Of(Schedule.Interval(5, T(9, 15), T(15, 30), false, Weekdays.Monday | Weekdays.Friday));

        (spec.Type, spec.From, spec.To, spec.EveryMinutes, spec.TradingDaysOnly).ShouldBe(("interval", "09:15", "15:30", 5, false));
        spec.Days.ShouldBe(["mon", "fri"]);
    }

    [Fact]
    public void An_unknown_type_is_an_error_on_the_type()
    {
        Parse(new ScheduleSpec("hourly"), out var issues).ShouldBeNull();

        issues.ShouldHaveSingleItem().ShouldBeEquivalentTo(ScheduleIssue.Error("svc", "schedule.type", ScheduleIssueCodes.TypeInvalid, issues[0].Message));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("8:45")]
    [InlineData("noon")]
    [InlineData("")]
    [InlineData(null)]
    public void An_invalid_or_missing_time_is_an_error_on_the_time(string? time)
    {
        Parse(new ScheduleSpec("daily", time, ["mon"]), out var issues).ShouldBeNull();

        issues.ShouldHaveSingleItem().Field.ShouldBe("schedule.time");
    }

    [Fact]
    public void A_window_with_unreadable_ends_names_both_fields()
    {
        Parse(new ScheduleSpec("window", From: "9", To: "x", Days: ["mon"]), out var issues).ShouldBeNull();

        issues.Select(i => i.Field).ShouldBe(["schedule.from", "schedule.to"]);
    }

    [Fact]
    public void An_unknown_day_is_an_error_and_a_weekly_schedule_needs_a_weekday()
    {
        Parse(new ScheduleSpec("daily", "10:00", ["mon", "funday"]), out var days).ShouldBeNull();
        days.ShouldHaveSingleItem().Field.ShouldBe("schedule.days");

        Parse(new ScheduleSpec("weekly", "10:00"), out var weekday).ShouldBeNull();
        weekday.ShouldHaveSingleItem().Field.ShouldBe("schedule.weekday");
    }

    [Fact]
    public void Trading_days_makes_the_days_monday_to_friday_and_fields_of_other_types_are_dropped()
    {
        var parsed = Parse(new ScheduleSpec("daily", "15:50", ["sat"], TradingDaysOnly: true, After: "x", EveryMinutes: 5), out _);

        parsed.ShouldBe(Schedule.Daily(T(15, 50)));
    }

    [Fact]
    public void A_schedule_that_follows_or_is_always_up_has_no_trading_days_flag()
    {
        Parse(new ScheduleSpec("continuous", TradingDaysOnly: true), out _)!.TradingDaysOnly.ShouldBeFalse();
        Parse(new ScheduleSpec("after", After: " eod "), out _).ShouldBe(Schedule.AfterService("eod"));
    }
}
