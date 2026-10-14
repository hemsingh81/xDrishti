using XDrishti.Application.Live;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Live;

/// <summary>The definition of the Live feed service for the Services screen (P2-10 criterion 15).</summary>
public sealed class LiveFeedServiceTests
{
    private static readonly ServiceDefinition Definition = LiveFeedService.Definition;

    [Fact]
    public void It_is_a_window_service_run_by_xd_feed_with_the_ids_and_names_the_screen_uses()
    {
        Definition.Id.ShouldBe("live-feed");
        Definition.RunsAs.ShouldBe("xd-feed");
        Definition.AllowedTypes.ShouldBe([ScheduleType.Window]);
    }

    [Fact]
    public void Its_defaults_are_a_window_from_nine_to_three_thirty_five_on_trading_days_without_retries_or_catch_up()
    {
        var defaults = Definition.Defaults;

        defaults.Enabled.ShouldBeTrue();
        defaults.Schedule.ShouldBe(Schedule.Window(new TimeOnly(9, 0), new TimeOnly(15, 35)));
        defaults.Schedule.TradingDaysOnly.ShouldBeTrue();
        defaults.Retries.ShouldBe(0);
        defaults.CatchUp.ShouldBeFalse();
        defaults.NotifyOnFailure.ShouldBeTrue();
        defaults.TimeoutMinutes.ShouldBe(ServiceSettings.MaxTimeoutMinutes);
    }

    [Fact]
    public void It_asks_for_the_window_to_cover_market_hours()
    {
        Definition.Constraints.MustCover.ShouldBe(TimeRange.MarketHours);
    }

    [Fact]
    public void Its_own_defaults_raise_no_issue_in_the_schedule_rules()
    {
        var subject = new RuleSubject(Definition, Definition.Defaults);

        ScheduleRules.Evaluate([subject]).ShouldBeEmpty();
    }

    [Fact]
    public void A_window_that_starts_late_is_warned_about_by_the_rules()
    {
        var late = new RuleSubject(Definition, Definition.Defaults with { Schedule = Schedule.Window(new TimeOnly(10, 0), new TimeOnly(15, 35)) });

        ScheduleRules.Evaluate([late]).ShouldNotBeEmpty();
    }
}
