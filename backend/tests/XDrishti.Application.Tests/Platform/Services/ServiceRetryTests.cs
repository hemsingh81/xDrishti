using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// Timeouts, retries and what happens to a sequence of retries around them (ADR 0018 §3, §5): a timeout is never retried and says when the job is still running; a restart during a back-off takes the
/// sequence up again; a pause drops a pending retry; a retry that cannot start is recorded; the failure notice waits until the sequence is over.
/// </summary>
public sealed class ServiceRetryTests : IDisposable
{
    private readonly TestService _job = Make("job", Schedule.Daily(T(15, 50)), retries: 2, timeout: 5);

    private readonly ServicesWorld _world;

    public ServiceRetryTests()
    {
        _world = new ServicesWorld(_job);
        _world.SetClock(At(12, 15, 51));
        _world.Application.StartedAt = At(12, 10, 0);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task TickAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    private ServiceRun FailedAttempt(int attempt, DateTimeOffset finishedAt, string message = "broken", ServiceRunTrigger trigger = ServiceRunTrigger.Scheduled)
    {
        var run = ServiceRun.Start("job", trigger, attempt, finishedAt.AddSeconds(-5));
        run.Fail(finishedAt, message);
        _world.Data.Runs.Add(run);
        return run;
    }

    private async Task<ServiceNotices> NoticesAsync() =>
        await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct));

    // ---- timeout ----

    [Fact]
    public async Task A_timeout_is_never_retried_even_with_retries_left()
    {
        _job.Behavior = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return ServiceOutcome.Succeeded();
        };
        await _world.Runner.StartAsync("job", ServiceRunTrigger.Scheduled, "x", Ct);
        await _job.WhenStartedAsync();

        _world.Clock.Advance(TimeSpan.FromMinutes(5));
        await _world.Runner.WhenIdleAsync();
        await TickAtAsync(_world.Clock.GetUtcNow() + TimeSpan.FromHours(3));

        var run = _world.Data.RunsOf("job").ShouldHaveSingleItem();
        (run.Status, run.Message, run.IsTimedOut).ShouldBe((ServiceRunStatus.Failed, "Timed out after 5 min.", true));
        _world.Runner.IsActive("job").ShouldBeFalse();
        _world.Runner.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_timeout_of_work_handed_to_a_background_worker_says_the_job_is_still_running()
    {
        _job.Behavior = async (context, ct) =>
        {
            context.MarkHandedOff();
            await Task.Delay(Timeout.Infinite, ct);
            return ServiceOutcome.Succeeded();
        };
        await _world.Runner.StartAsync("job", ServiceRunTrigger.Manual, "x", Ct);
        await _job.WhenStartedAsync();

        _world.Clock.Advance(TimeSpan.FromMinutes(5));
        await _world.Runner.WhenIdleAsync();

        var run = _world.Data.RunsOf("job").Single();
        run.Message.ShouldBe("Timed out after 5 min (the job is still running).");
        _world.Runner.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_failure_that_is_not_a_timeout_is_still_retried()
    {
        _job.AlwaysFail("broken");
        await _world.Runner.StartAsync("job", ServiceRunTrigger.Scheduled, "x", Ct);
        await _world.Runner.WhenIdleAsync();

        _world.Runner.NextRetryAt.ShouldBe(_world.Clock.GetUtcNow() + TimeSpan.FromSeconds(30));
    }

    // ---- resume after a restart ----

    [Fact]
    public async Task A_restart_during_a_back_off_takes_the_sequence_up_again_with_the_original_trigger_and_the_next_attempt()
    {
        FailedAttempt(1, At(12, 15, 50, 30));
        _world.SetClock(At(12, 15, 50, 40));
        _world.Application.StartedAt = At(12, 15, 50, 40);

        await _world.Scheduler.StartUpAsync(Ct);
        _job.Calls.ShouldBeEmpty(); // the back-off is not over: 30 s after the failure
        _world.Runner.NextRetryAt.ShouldBe(At(12, 15, 51, 0));
        await TickAtAsync(At(12, 15, 51, 0));

        var call = _job.Calls.ShouldHaveSingleItem();
        (call.Attempt, call.Trigger, call.RequestedBy).ShouldBe((2, ServiceRunTrigger.Scheduled, ServiceRunner.RetryRequester));
        _world.Data.RunsOf("job").Select(r => (r.Attempt, r.Status)).ShouldBe([(1, ServiceRunStatus.Failed), (2, ServiceRunStatus.Succeeded)]);
    }

    [Fact]
    public async Task A_resumed_sequence_goes_on_to_the_last_attempt_and_no_further()
    {
        FailedAttempt(2, At(12, 15, 50, 30));
        _job.AlwaysFail("still broken");
        _world.SetClock(At(12, 15, 50, 40));
        _world.Application.StartedAt = At(12, 15, 50, 40);
        await _world.Scheduler.StartUpAsync(Ct);

        await TickAtAsync(At(12, 15, 52, 30)); // attempt 3 (retries = 2) after the 2 minutes back-off
        await TickAtAsync(At(12, 18, 0));

        _world.Data.RunsOf("job").Select(r => r.Attempt).ShouldBe([2, 3]);
    }

    public static TheoryData<string, int, int, string> NotResumed => new()
    {
        { "all the attempts are used", 3, 1, "broken" },
        { "interrupted", 1, 1, "Interrupted: the application stopped or crashed while the service ran." },
        { "timed out", 1, 1, "Timed out after 5 min." },
        { "long ago", 1, 1, "broken" },
    };

    [Theory]
    [MemberData(nameof(NotResumed))]
    public async Task A_sequence_that_is_over_is_not_resumed(string why, int attempt, int unused, string message)
    {
        _ = unused;
        FailedAttempt(attempt, why == "long ago" ? At(12, 12, 0) : At(12, 15, 50, 30), message);
        _world.SetClock(At(12, 15, 50, 40));
        _world.Application.StartedAt = At(12, 15, 50, 40);

        await _world.Scheduler.StartUpAsync(Ct);
        await TickAtAsync(At(12, 15, 59, 0));

        _world.Runner.NextRetryAt.ShouldBeNull();
        _world.Data.RunsOf("job").Any(r => r.Attempt > attempt).ShouldBeFalse();
    }

    [Fact]
    public async Task A_paused_service_is_not_resumed_and_a_successful_run_after_the_failure_ends_the_sequence()
    {
        FailedAttempt(1, At(12, 15, 50, 30));
        _world.Data.Store("job", Settings(Schedule.Daily(T(15, 50)), enabled: false, retries: 2));
        _world.SetClock(At(12, 15, 50, 40));
        _world.Application.StartedAt = At(12, 15, 50, 40);

        await _world.Scheduler.StartUpAsync(Ct);

        _world.Runner.NextRetryAt.ShouldBeNull();
    }

    // ---- a pending retry meets a pause or a busy service ----

    [Fact]
    public async Task A_pending_retry_is_dropped_when_the_service_was_paused_meanwhile()
    {
        _job.AlwaysFail("broken");
        await _world.Runner.StartAsync("job", ServiceRunTrigger.Scheduled, "x", Ct);
        await _world.Runner.WhenIdleAsync();
        _world.Runner.NextRetryAt.ShouldNotBeNull();

        (await _world.PauseAsync("job")).IsSuccess.ShouldBeTrue();
        await TickAtAsync(_world.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        _world.Data.RunsOf("job").ShouldHaveSingleItem();
        _world.Runner.IsActive("job").ShouldBeFalse();
        _world.Runner.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_retry_that_cannot_start_leaves_a_skipped_run_and_ends_the_sequence()
    {
        _job.AlwaysFail("broken");
        await _world.Runner.StartAsync("job", ServiceRunTrigger.Scheduled, "x", Ct);
        await _world.Runner.WhenIdleAsync();
        _world.Data.RefuseStarts = true; // the database has a running run of the service (another process)

        await TickAtAsync(_world.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        var skipped = _world.Data.RunsOf("job").Last();
        (skipped.Status, skipped.Message).ShouldBe((ServiceRunStatus.Skipped, "Retry not started: the service was running already."));
        _world.Runner.IsActive("job").ShouldBeFalse();
    }

    // ---- the request token ----

    [Fact]
    public async Task The_run_record_is_not_written_under_the_token_of_the_request_that_asked_for_it()
    {
        using var request = new CancellationTokenSource();

        await _world.Runner.StartAsync("job", ServiceRunTrigger.Manual, "owner", request.Token);
        await request.CancelAsync(); // the client disconnects
        await _world.Runner.WhenIdleAsync();

        _world.Data.StartToken.IsCancellationRequested.ShouldBeFalse();
        _world.Data.StartToken.CanBeCanceled.ShouldBeTrue(); // it is the application's own token
        _world.Data.RunsOf("job").Single().Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    [Fact]
    public async Task A_request_that_is_cancelled_before_the_start_starts_nothing()
    {
        using var request = new CancellationTokenSource();
        await request.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await _world.Runner.StartAsync("job", ServiceRunTrigger.Manual, "owner", request.Token));

        _world.Data.Runs.ShouldBeEmpty();
        _world.Runner.IsActive("job").ShouldBeFalse();
    }

    // ---- the failure notice waits for the end of the sequence ----

    [Fact]
    public async Task The_notice_waits_while_a_retry_is_pending_and_comes_when_the_sequence_is_over()
    {
        FailedAttempt(1, At(12, 15, 50, 30));
        _world.SetClock(At(12, 15, 51));
        (await NoticesAsync()).Items.ShouldBeEmpty(); // attempt 1 of 3

        _world.SetClock(At(12, 17, 30)); // still within the two hours in which a retry may follow
        (await NoticesAsync()).Items.ShouldBeEmpty();
        _world.SetClock(At(12, 18, 0)); // the retry never came: the sequence is over
        (await NoticesAsync()).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_notice_is_there_at_once_for_a_timeout_the_last_attempt_and_a_paused_service()
    {
        FailedAttempt(1, At(12, 15, 50, 30), "Timed out after 5 min (the job is still running).");
        _world.SetClock(At(12, 15, 51));
        (await NoticesAsync()).Items.ShouldHaveSingleItem();

        _world.Data.Runs.Clear();
        FailedAttempt(3, At(12, 15, 50, 30));
        (await NoticesAsync()).Items.ShouldHaveSingleItem();

        _world.Data.Runs.Clear();
        FailedAttempt(1, At(12, 15, 50, 30));
        (await NoticesAsync()).Items.ShouldBeEmpty();
        await _world.PauseAsync("job");
        (await NoticesAsync()).Items.ShouldHaveSingleItem();
    }
}
