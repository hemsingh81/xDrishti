using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// A service that another process runs (the live feed, <see cref="IExternallyRunService"/>, P2-10 criteria 8 and 15): the Services screen shows and edits it through the stored row, but the scheduler and the runner of this process never start
/// it, retry it or close its runs; Run now writes the manual-run marker; its status comes from the heartbeat. Monday 2026-10-12 is a trading day.
/// </summary>
public sealed class ExternalServiceTests : IDisposable
{
    private readonly TestService _other = Make("other", Schedule.Daily(T(10, 30)), retries: 0, timeout: 30);
    private readonly ServicesWorld _world;

    public ExternalServiceTests() => _world = new ServicesWorld((_, _) => [_other, new LiveFeedService()], _ => { });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private IReadOnlyList<ServiceRun> FeedRuns => [.. _world.Data.RunsOf(LiveFeedService.Id)];

    private static FeedStatus Alive(string state = FeedStates.Closed) => new(state, At(12, 10, 0), true, new FeedHeartbeat(state == FeedStates.Connected, 12, null, 0, 0, 0, 0));

    private async Task StartUpAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        _world.Application.StartedAt = instant;
        await _world.Scheduler.StartUpAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private async Task TickAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    // ---- the scheduler and the runner leave it alone ----

    [Fact]
    public async Task The_scheduler_never_starts_the_live_feed_at_its_window_start()
    {
        await StartUpAtAsync(At(12, 8, 58));

        await TickAtAsync(At(12, 9, 0));
        await TickAtAsync(At(12, 9, 1));
        await TickAtAsync(At(12, 10, 0));

        FeedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_scheduler_still_starts_the_other_services()
    {
        await StartUpAtAsync(At(12, 10, 0));

        await TickAtAsync(At(12, 10, 30));

        _other.Calls.ShouldHaveSingleItem();
        FeedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_start_up_inside_the_window_does_not_make_up_for_the_live_feed_even_with_catch_up_on()
    {
        _world.Data.Store(LiveFeedService.Id, LiveFeedService.Definition.Defaults with { CatchUp = true });

        await StartUpAtAsync(At(12, 11, 0));
        await TickAtAsync(At(12, 11, 1));

        FeedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_start_up_does_not_close_the_open_run_of_the_live_feed_but_closes_the_interrupted_runs_of_the_others()
    {
        var feed = ServiceRun.Start(LiveFeedService.Id, ServiceRunTrigger.Scheduled, 1, At(12, 9, 0));
        var other = ServiceRun.Start("other", ServiceRunTrigger.Scheduled, 1, At(12, 9, 5));
        _world.Data.Runs.AddRange([feed, other]);

        await StartUpAtAsync(At(12, 10, 0));

        feed.Status.ShouldBe(ServiceRunStatus.Running); // xd-feed's window run survives a restart of the API; the feed closes stale runs itself
        other.Status.ShouldBe(ServiceRunStatus.Failed);
        other.Message.ShouldStartWith("Interrupted");
    }

    [Fact]
    public async Task The_runner_refuses_to_start_the_live_feed()
    {
        var start = await _world.Runner.StartAsync(LiveFeedService.Id, ServiceRunTrigger.Manual, "owner@example.invalid", Ct);

        start.RunsElsewhere.ShouldBeTrue();
        start.Run.ShouldBeNull();
        start.AlreadyRunning.ShouldBeFalse();
        start.Unknown.ShouldBeFalse();
        FeedRuns.ShouldBeEmpty();
        _world.Runner.IsActive(LiveFeedService.Id).ShouldBeFalse();
    }

    [Fact]
    public void A_retry_cannot_be_resumed_for_the_live_feed()
    {
        _world.Runner.ResumeRetry(LiveFeedService.Id, ServiceRunTrigger.Scheduled, ServiceRunner.RetryRequester, 2, At(12, 10, 0)).ShouldBeFalse();
        _world.Runner.NextRetryAt.ShouldBeNull();
    }

    // ---- shown and edited like the others ----

    [Fact]
    public async Task The_list_shows_the_live_feed_as_run_by_xd_feed_with_its_window()
    {
        _world.Heartbeats.Status = Alive();

        var view = await _world.ViewAsync(LiveFeedService.Id);

        view.RunsAs.ShouldBe("xd-feed");
        view.Name.ShouldBe("Live feed");
        view.Schedule.Type.ShouldBe("window");
        view.AllowedScheduleTypes.ShouldBe(["window"]);
        view.Live.ShouldNotBeNull().State.ShouldBe("closed");
    }

    [Fact]
    public async Task Pause_and_resume_change_the_stored_row_that_the_feed_reads()
    {
        _world.Heartbeats.Status = Alive(FeedStates.Paused);
        _world.SetClock(At(12, 18, 0));

        var paused = (await _world.PauseAsync(LiveFeedService.Id)).Value;
        var resumed = (await _world.ResumeAsync(LiveFeedService.Id)).Value;

        paused.Enabled.ShouldBeFalse();
        paused.Status.ShouldBe("paused");
        resumed.Enabled.ShouldBeTrue();
        _world.Data.Rows[LiveFeedService.Id].Enabled.ShouldBeTrue();
        _world.Data.Events.Select(e => e.Event).ShouldBe([AccountEventType.ServicePaused, AccountEventType.ServiceResumed]);
    }

    [Fact]
    public async Task The_status_follows_the_run_and_the_heartbeat()
    {
        _world.SetClock(At(12, 10, 0, 20));
        _world.Heartbeats.Status = Alive(FeedStates.Connected);
        _world.Data.Runs.Add(ServiceRun.Start(LiveFeedService.Id, ServiceRunTrigger.Scheduled, 1, At(12, 9, 0)));

        (await _world.ViewAsync(LiveFeedService.Id)).Status.ShouldBe("running");

        _world.Heartbeats.Status = Alive(FeedStates.Connected) with { IsAlive = false };

        var failed = await _world.ViewAsync(LiveFeedService.Id);
        failed.Status.ShouldBe("failed");
        failed.Live.ShouldNotBeNull().Summary.ShouldBe("The live feed is not running.");
    }

    [Fact]
    public async Task The_other_services_have_no_live_details()
    {
        (await _world.ViewAsync("other")).Live.ShouldBeNull();
    }

    // ---- Run now ----

    [Fact]
    public async Task Run_now_writes_the_manual_marker_audits_the_request_and_answers_with_a_queued_run()
    {
        _world.SetClock(At(12, 18, 0));

        var result = await _world.RunNowAsync(LiveFeedService.Id);

        var run = result.Value;
        run.Status.ShouldBe("queued");
        run.Id.ShouldBe(Guid.Empty);
        run.ServiceId.ShouldBe(LiveFeedService.Id);
        run.Trigger.ShouldBe("manual");
        run.FinishedAt.ShouldBeNull();
        run.Message.ShouldNotBeNull().ShouldContain("15 seconds");
        _world.Manual.Until.ShouldBe(At(12, 18, 30)); // 30 minutes, the default
        _world.Manual.Actor.ShouldBe("owner@example.invalid");
        _world.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.ServiceRunRequested);
        FeedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_now_is_refused_while_the_feed_has_a_run_open_and_leaves_the_marker_alone()
    {
        _world.Data.Runs.Add(ServiceRun.Start(LiveFeedService.Id, ServiceRunTrigger.Scheduled, 1, At(12, 9, 0)));

        var result = await _world.RunNowAsync(LiveFeedService.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ServiceErrors.AlreadyRunningCode);
        _world.Manual.Until.ShouldBeNull();
        _world.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_now_works_on_a_paused_live_feed()
    {
        await _world.PauseAsync(LiveFeedService.Id);

        var result = await _world.RunNowAsync(LiveFeedService.Id);

        result.IsSuccess.ShouldBeTrue();
        _world.Manual.Until.ShouldNotBeNull();
    }

    [Fact]
    public async Task Pausing_the_live_feed_ends_a_manual_run_in_force_so_that_pausing_really_pauses()
    {
        _world.SetClock(At(12, 18, 0));
        await _world.RunNowAsync(LiveFeedService.Id);
        _world.Manual.Until.ShouldBe(At(12, 18, 30));

        await _world.PauseAsync(LiveFeedService.Id);

        _world.Manual.Until.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(At(12, 18, 0)); // no longer in the future: the feed is not wanted up any more
        _world.Manual.Actor.ShouldBe("owner@example.invalid");
    }

    [Fact]
    public async Task Pausing_a_live_feed_that_is_paused_already_still_ends_the_manual_run_that_keeps_it_up()
    {
        _world.SetClock(At(12, 18, 0));
        await _world.PauseAsync(LiveFeedService.Id);
        await _world.RunNowAsync(LiveFeedService.Id);

        var again = await _world.PauseAsync(LiveFeedService.Id);

        again.IsSuccess.ShouldBeTrue();
        _world.Manual.Until.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(At(12, 18, 0));
    }

    [Fact]
    public async Task Saving_the_live_feed_switched_off_also_ends_the_manual_run_but_saving_it_on_does_not()
    {
        _world.SetClock(At(12, 18, 0));
        await _world.RunNowAsync(LiveFeedService.Id);
        var window = Schedule.Window(T(9, 0), T(15, 35));

        var on = await _world.SaveAsync(LiveFeedService.Id, Input(window, enabled: true, timeout: 240));
        on.IsSuccess.ShouldBeTrue(on.IsFailure ? on.Error.Code : string.Empty);
        _world.Manual.Until.ShouldBe(At(12, 18, 30));

        var off = await _world.SaveAsync(LiveFeedService.Id, Input(window, revision: on.Value.Revision, enabled: false, timeout: 240));

        off.IsSuccess.ShouldBeTrue(off.IsFailure ? off.Error.Code : string.Empty);
        _world.Manual.Until.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(At(12, 18, 0));
    }

    [Fact]
    public async Task Pausing_a_service_this_application_runs_leaves_the_marker_alone()
    {
        _world.SetClock(At(12, 18, 0));
        await _world.RunNowAsync(LiveFeedService.Id);

        await _world.PauseAsync("other");

        _world.Manual.Until.ShouldBe(At(12, 18, 30));
    }
}
