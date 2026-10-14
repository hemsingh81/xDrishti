using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Tests.Live.Push;

/// <summary>The feed status the screen gets, from the feed's heartbeat (same reading as the Services screen).</summary>
public sealed class LiveStatusMapperTests
{
    private static readonly DateTimeOffset Seen = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    private static FeedHeartbeat Beat(int subscribed = 40, DateTimeOffset? lastTick = null) => new(true, subscribed, lastTick, 12.5, 0, 0, 3);

    [Fact]
    public void A_feed_that_never_reported_is_not_running()
    {
        LiveStatusMapper.Map(null).ShouldBe(new LiveFeedStatus("not_running", 0, null));
    }

    [Fact]
    public void A_live_feed_reports_its_own_state_the_instruments_it_follows_and_its_latest_tick()
    {
        var tick = Seen.AddSeconds(-2);

        var status = LiveStatusMapper.Map(new FeedStatus(FeedStates.Connected, Seen, true, Beat(40, tick)));

        status.ShouldBe(new LiveFeedStatus("connected", 40, tick));
    }

    [Theory]
    [InlineData(FeedStates.Idle)]
    [InlineData(FeedStates.Closed)]
    [InlineData(FeedStates.Paused)]
    [InlineData(FeedStates.NoToken)]
    [InlineData(FeedStates.TokenRefused)]
    [InlineData(FeedStates.Flapping)]
    [InlineData(FeedStates.Connecting)]
    [InlineData(FeedStates.Reconnecting)]
    public void The_other_states_of_a_feed_that_reports_are_passed_on_as_they_are(string state)
    {
        LiveStatusMapper.Map(new FeedStatus(state, Seen, true, Beat(0))).State.ShouldBe(state);
    }

    [Fact]
    public void A_feed_that_has_stopped_reporting_is_not_running_but_its_last_tick_is_still_known()
    {
        var tick = Seen.AddMinutes(-3);

        var status = LiveStatusMapper.Map(new FeedStatus(FeedStates.Connected, Seen.AddMinutes(-5), false, Beat(40, tick)));

        status.ShouldBe(new LiveFeedStatus("not_running", 0, tick));
    }

    [Fact]
    public void A_feed_that_stopped_on_purpose_is_not_running()
    {
        var status = LiveStatusMapper.Map(new FeedStatus(FeedStates.Stopped, Seen, true, Beat(40)));

        status.State.ShouldBe("not_running");
        status.Subscribed.ShouldBe(0);
    }

    [Fact]
    public void A_heartbeat_without_details_is_a_feed_that_is_alive_and_says_nothing_else()
    {
        LiveStatusMapper.Map(new FeedStatus(FeedStates.Connected, Seen, true, null)).ShouldBe(new LiveFeedStatus("connected", 0, null));
    }
}
