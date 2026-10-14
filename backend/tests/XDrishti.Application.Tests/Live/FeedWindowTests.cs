using XDrishti.Application.Live;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Live;

/// <summary>When the feed is wanted up (ADR 0024 §1): the window of the schedule on trading days, a pause, a manual run.</summary>
public sealed class FeedWindowTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly TradingCalendar Calendar = new();

    private static ServiceSettings Settings(bool enabled = true) => LiveFeedService.Definition.Defaults with { Enabled = enabled };

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, Ist);

    [Theory]
    [InlineData(12, 8, 59, false)] // Monday before the window
    [InlineData(12, 9, 0, true)] // the start is in
    [InlineData(12, 12, 0, true)]
    [InlineData(12, 15, 34, true)]
    [InlineData(12, 15, 35, false)] // the end is out
    [InlineData(12, 20, 0, false)]
    [InlineData(17, 10, 0, false)] // Saturday: not a trading day
    public void The_feed_is_up_inside_the_window_of_a_trading_day_only(int day, int hour, int minute, bool expected)
    {
        FeedWindow.Decide(Settings(), Calendar, At(day, hour, minute), null).ShouldBe(new FeedWindowDecision(expected, false, FeedStates.Closed));
    }

    [Fact]
    public void A_paused_service_is_down_and_says_paused()
    {
        FeedWindow.Decide(Settings(enabled: false), Calendar, At(12, 10, 0), null).ShouldBe(new FeedWindowDecision(false, false, FeedStates.Paused));
    }

    [Fact]
    public void A_manual_run_in_force_brings_the_feed_up_outside_the_window_and_while_paused()
    {
        var until = At(17, 11, 0);

        FeedWindow.Decide(Settings(), Calendar, At(17, 10, 0), until).ShouldBe(new FeedWindowDecision(true, true, FeedStates.Closed));
        FeedWindow.Decide(Settings(enabled: false), Calendar, At(17, 10, 0), until).ShouldBe(new FeedWindowDecision(true, true, FeedStates.Closed));
    }

    [Fact]
    public void A_manual_run_that_has_passed_counts_for_nothing()
    {
        FeedWindow.Decide(Settings(), Calendar, At(17, 12, 0), At(17, 11, 0)).ShouldBe(new FeedWindowDecision(false, false, FeedStates.Closed));
    }
}
