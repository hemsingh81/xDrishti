using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Live.SupervisorWorld;

namespace XDrishti.Application.Tests.Live;

/// <summary>The status of the live feed's row on the Services screen (P2-10 criterion 15): the stored row, the latest run and the feed's heartbeat, in and out of the window. Monday 2026-10-12 is a trading day.</summary>
public sealed class LiveFeedHealthTests
{
    private static readonly TradingCalendar Calendar = new();

    private static EffectiveService Service(bool enabled = true) => new(LiveFeedService.Definition, LiveFeedService.Definition.Defaults with { Enabled = enabled }, null);

    private static FeedStatus Beat(string state, bool alive = true, DateTimeOffset? tick = null, int subscribed = 58, double rate = 4.25, DateTimeOffset? seen = null) =>
        new(state, seen ?? At(12, 10, 0), alive, new FeedHeartbeat(state == FeedStates.Connected, subscribed, tick, rate, 0, 0, 3));

    private static ServiceRun Finished(ServiceRunStatus status)
    {
        var run = ServiceRun.Start(LiveFeedService.Id, ServiceRunTrigger.Scheduled, 1, At(11, 9, 0));
        if (status == ServiceRunStatus.Failed)
        {
            run.Fail(At(11, 9, 30), "Token refused");
        }
        else
        {
            run.Succeed(At(11, 15, 35), "Window ended");
        }

        return run;
    }

    private static LiveFeedReading Read(
        DateTimeOffset now,
        FeedStatus? heartbeat,
        bool enabled = true,
        bool runOpen = false,
        ServiceRun? last = null,
        DateTimeOffset? manualUntil = null) =>
        LiveFeedHealth.Evaluate(Service(enabled), last, runOpen, Calendar, now, heartbeat, manualUntil);

    [Fact]
    public void An_open_run_with_a_feed_that_reports_is_running_and_the_summary_carries_the_figures()
    {
        var reading = Read(At(12, 10, 0, 15), Beat(FeedStates.Connected, tick: At(12, 10, 0, 12)), runOpen: true);

        reading.Status.ShouldBe("running");
        reading.Live.State.ShouldBe("connected");
        reading.Live.Connected.ShouldBeTrue();
        reading.Live.Subscribed.ShouldBe(58);
        reading.Live.LastTickAt.ShouldBe(At(12, 10, 0, 12));
        reading.Live.TicksPerSecond.ShouldBe(4.25);
        reading.Live.Summary.ShouldBe("Connected · 58 instruments · last tick 10:00:12 IST · 4.3 ticks/s");
    }

    [Fact]
    public void A_connected_feed_without_a_tick_yet_says_so()
    {
        var reading = Read(At(12, 9, 1), Beat(FeedStates.Connected, rate: 0), runOpen: true);

        reading.Live.Summary.ShouldBe("Connected · 58 instruments · no tick yet · 0 ticks/s");
    }

    [Fact]
    public void In_the_window_a_heartbeat_older_than_a_minute_is_a_failure_that_says_the_feed_is_not_running()
    {
        var reading = Read(At(12, 10, 0), Beat(FeedStates.Connected, alive: false, seen: At(12, 9, 50)), runOpen: true);

        reading.Status.ShouldBe("failed");
        reading.Live.State.ShouldBe("not_running");
        reading.Live.Connected.ShouldBeFalse();
        reading.Live.Summary.ShouldBe("The live feed is not running.");
        reading.Live.LastSeenAt.ShouldBe(At(12, 9, 50));
    }

    [Fact]
    public void In_the_window_a_feed_that_never_reported_is_a_failure()
    {
        var reading = Read(At(12, 10, 0), null);

        reading.Status.ShouldBe("failed");
        reading.Live.Summary.ShouldBe(LiveFeedHealth.NotRunning);
        reading.Live.LastSeenAt.ShouldBeNull();
    }

    [Fact]
    public void A_feed_that_stopped_on_purpose_inside_the_window_is_not_running_even_within_a_minute_of_its_last_beat()
    {
        var reading = Read(At(12, 10, 0), Beat(FeedStates.Stopped));

        reading.Status.ShouldBe("failed");
        reading.Live.Summary.ShouldBe(LiveFeedHealth.NotRunning);
    }

    [Fact]
    public void Outside_the_window_an_idle_feed_is_healthy_and_says_idle()
    {
        var reading = Read(At(12, 18, 0), Beat(FeedStates.Closed), last: Finished(ServiceRunStatus.Succeeded));

        reading.Status.ShouldBe("healthy");
        reading.Live.Summary.ShouldBe("Idle: outside the window.");
    }

    [Fact]
    public void Outside_the_window_a_feed_that_is_not_reporting_is_healthy_and_idle_not_a_failure()
    {
        var reading = Read(At(12, 18, 0), Beat(FeedStates.Closed, alive: false), last: Finished(ServiceRunStatus.Succeeded));

        reading.Status.ShouldBe("healthy");
        reading.Live.Summary.ShouldBe("Idle: outside the window.");
    }

    [Fact]
    public void On_a_day_without_a_session_the_feed_is_not_wanted_and_is_healthy()
    {
        var reading = Read(At(10, 10, 0), null); // Saturday

        reading.Status.ShouldBe("healthy");
    }

    [Fact]
    public void A_failed_latest_run_keeps_the_failure_outside_the_window()
    {
        var reading = Read(At(12, 18, 0), Beat(FeedStates.Closed), last: Finished(ServiceRunStatus.Failed));

        reading.Status.ShouldBe("failed");
    }

    [Fact]
    public void A_paused_service_is_paused_whether_or_not_the_feed_reports()
    {
        Read(At(12, 10, 0), Beat(FeedStates.Paused), enabled: false).Status.ShouldBe("paused");
        Read(At(12, 10, 0), null, enabled: false).Status.ShouldBe("paused");
        Read(At(12, 10, 0), Beat(FeedStates.Paused), enabled: false).Live.Summary.ShouldBe("Paused.");
    }

    [Fact]
    public void A_feed_without_a_token_is_healthy_because_its_run_is_recorded_as_skipped()
    {
        var reading = Read(At(12, 9, 30), Beat(FeedStates.NoToken));

        reading.Status.ShouldBe("healthy");
        reading.Live.Summary.ShouldBe("No usable token: connect the Data account.");
    }

    [Fact]
    public void A_manual_run_outside_the_window_wants_the_feed_up()
    {
        var running = Read(At(12, 18, 0), Beat(FeedStates.Connected), runOpen: true, manualUntil: At(12, 18, 30));
        var missing = Read(At(12, 18, 0), null, manualUntil: At(12, 18, 30));
        var over = Read(At(12, 18, 31), null, manualUntil: At(12, 18, 30));

        running.Status.ShouldBe("running");
        missing.Status.ShouldBe("failed");
        over.Status.ShouldBe("healthy");
    }

    [Theory]
    [InlineData(FeedStates.Connecting, "Connecting.")]
    [InlineData(FeedStates.Reconnecting, "Reconnecting.")]
    [InlineData(FeedStates.Idle, "Connected · nothing to follow.")]
    [InlineData(FeedStates.TokenRefused, "The Data account's token was refused.")]
    [InlineData(FeedStates.Flapping, "Reconnecting too often; waiting 5 minutes.")]
    [InlineData(FeedStates.Closed, "Starting.")]
    public void Each_state_of_the_feed_has_its_own_sentence_in_the_window(string state, string summary) =>
        Read(At(12, 10, 0), Beat(state)).Live.Summary.ShouldBe(summary);
}
