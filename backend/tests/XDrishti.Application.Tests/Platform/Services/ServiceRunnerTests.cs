using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// Carrying out runs (ADR 0018 §3, §5): one at a time, a skipped run recorded for a start while it runs, every attempt a run record, retries with the back-off 30 s / 2 min / 10 min / 30 min / 1 h, the
/// timeout, a service that throws, the link to a data job and a stopping application.
/// </summary>
public sealed class ServiceRunnerTests : IDisposable
{
    private readonly TestService _service;

    private readonly ServicesWorld _world;

    public ServiceRunnerTests()
    {
        _service = Make("job", Schedule.Daily(T(15, 50)), retries: 3, timeout: 30);
        _world = new ServicesWorld(_service);
        _world.SetClock(At(12, 12, 0));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task<ServiceStart> StartAsync(ServiceRunTrigger trigger = ServiceRunTrigger.Manual) =>
        await _world.Runner.StartAsync("job", trigger, "owner@example.invalid", Ct);

    /// <summary>Moves the clock and lets the scheduler start what is due (the retries).</summary>
    private async Task AdvanceAsync(TimeSpan by)
    {
        _world.SetClock(_world.Clock.GetUtcNow() + by);
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();
    }

    // ---- one at a time ----

    [Fact]
    public async Task A_start_while_the_service_runs_starts_nothing_and_is_recorded_as_skipped()
    {
        var gate = _service.Gate();
        var first = await StartAsync();
        await _service.WhenStartedAsync();

        var second = await StartAsync(ServiceRunTrigger.Scheduled);

        second.AlreadyRunning.ShouldBeTrue();
        _service.Calls.Count.ShouldBe(1);
        var skipped = _world.Data.RunsOf("job").Single(r => r.Status == ServiceRunStatus.Skipped);
        (skipped.Trigger, skipped.Message).ShouldBe((ServiceRunTrigger.Scheduled, ServiceRunner.AlreadyRunningMessage));
        gate.SetResult(ServiceOutcome.Succeeded());
        await _world.Runner.WhenIdleAsync();
        first.Run!.Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    [Fact]
    public async Task A_running_record_in_the_database_also_refuses_a_start()
    {
        _world.Data.Runs.Add(ServiceRun.Start("job", ServiceRunTrigger.Manual, 1, At(12, 11, 59))); // another instance

        var start = await StartAsync();

        start.AlreadyRunning.ShouldBeTrue();
        _service.Calls.ShouldBeEmpty();
        _world.Runner.IsActive("job").ShouldBeFalse();
    }

    [Fact]
    public async Task A_service_can_run_again_once_the_run_has_ended()
    {
        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        var again = await StartAsync();
        await _world.Runner.WhenIdleAsync();

        again.AlreadyRunning.ShouldBeFalse();
        _service.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_service_is_not_started()
    {
        (await _world.Runner.StartAsync("nobody", ServiceRunTrigger.Manual, "x", Ct)).Unknown.ShouldBeTrue();
    }

    // ---- retries ----

    [Fact]
    public async Task A_failed_attempt_is_retried_with_the_back_off_and_every_attempt_is_a_run_record()
    {
        _service.AlwaysFail("Dhan refused the token");
        await StartAsync(ServiceRunTrigger.Scheduled);
        await _world.Runner.WhenIdleAsync();
        _world.Data.RunsOf("job").Count().ShouldBe(1);

        await AdvanceAsync(TimeSpan.FromSeconds(29));
        _world.Data.RunsOf("job").Count().ShouldBe(1);
        await AdvanceAsync(TimeSpan.FromSeconds(1)); // 30 s after the first failure
        _world.Data.RunsOf("job").Count().ShouldBe(2);
        await AdvanceAsync(TimeSpan.FromMinutes(2)); // the second back-off
        _world.Data.RunsOf("job").Count().ShouldBe(3);
        await AdvanceAsync(TimeSpan.FromMinutes(10)); // the third and last retry (retries = 3)
        var runs = _world.Data.RunsOf("job").ToList();

        runs.Select(r => r.Attempt).ShouldBe([1, 2, 3, 4]);
        runs.ShouldAllBe(r => r.Status == ServiceRunStatus.Failed && r.Message == "Dhan refused the token" && r.Trigger == ServiceRunTrigger.Scheduled);
        await AdvanceAsync(TimeSpan.FromHours(3));
        _world.Data.RunsOf("job").Count().ShouldBe(4); // exhausted
        _world.Runner.IsActive("job").ShouldBeFalse();
    }

    [Fact]
    public async Task The_back_off_is_30_seconds_2_minutes_10_minutes_30_minutes_and_one_hour()
    {
        ServiceRunner.Backoff.ShouldBe([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1)]);
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(15, 50)), retries: 5));
        var service = world.Services[0];
        service.AlwaysFail("x");
        world.SetClock(At(12, 12, 0));
        await world.Runner.StartAsync("job", ServiceRunTrigger.Manual, "x", Ct);
        await world.Runner.WhenIdleAsync();

        var starts = new List<DateTimeOffset> { world.Clock.GetUtcNow() };
        foreach (var wait in ServiceRunner.Backoff)
        {
            world.SetClock(world.Clock.GetUtcNow() + wait - TimeSpan.FromSeconds(1));
            await world.Scheduler.TickAsync(Ct);
            await world.Runner.WhenIdleAsync();
            var before = world.Data.RunsOf("job").Count();
            world.SetClock(world.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
            await world.Scheduler.TickAsync(Ct);
            await world.Runner.WhenIdleAsync();
            world.Data.RunsOf("job").Count().ShouldBe(before + 1);
            starts.Add(world.Clock.GetUtcNow());
        }

        starts.Zip(starts.Skip(1), (a, b) => b - a).ShouldBe(ServiceRunner.Backoff);
        world.Data.RunsOf("job").Count().ShouldBe(6);
    }

    [Fact]
    public async Task Success_ends_the_sequence()
    {
        var calls = 0;
        _service.Behavior = (_, _) => ValueTask.FromResult(++calls < 2 ? ServiceOutcome.Failed("first") : ServiceOutcome.Succeeded("worked"));
        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        await AdvanceAsync(TimeSpan.FromSeconds(30));
        await AdvanceAsync(TimeSpan.FromHours(3));

        var runs = _world.Data.RunsOf("job").ToList();
        runs.Select(r => (r.Attempt, r.Status, r.Message)).ShouldBe([(1, ServiceRunStatus.Failed, "first"), (2, ServiceRunStatus.Succeeded, "worked")]);
        _world.Runner.IsActive("job").ShouldBeFalse();
    }

    [Fact]
    public async Task Without_retries_a_failure_ends_the_run()
    {
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(15, 50)), retries: 0));
        world.Services[0].AlwaysFail("broken");
        world.SetClock(At(12, 12, 0));

        await world.Runner.StartAsync("job", ServiceRunTrigger.Manual, "x", Ct);
        await world.Runner.WhenIdleAsync();
        world.SetClock(At(12, 14, 0));
        await world.Scheduler.TickAsync(Ct);

        world.Data.RunsOf("job").ShouldHaveSingleItem().Status.ShouldBe(ServiceRunStatus.Failed);
        world.Runner.IsActive("job").ShouldBeFalse();
    }

    [Fact]
    public async Task A_skipped_attempt_is_never_retried()
    {
        _service.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Skipped("The Data account is not connected."));
        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        await AdvanceAsync(TimeSpan.FromHours(2));

        var run = _world.Data.RunsOf("job").ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, "The Data account is not connected."));
    }

    [Fact]
    public async Task While_a_retry_waits_the_service_counts_as_running_and_a_start_is_refused()
    {
        _service.AlwaysFail("x");
        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        var start = await StartAsync();

        start.AlreadyRunning.ShouldBeTrue();
        _world.Runner.IsActive("job").ShouldBeTrue();
        _world.Runner.NextRetryAt.ShouldBe(_world.Clock.GetUtcNow() + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task The_retries_are_read_again_at_the_failure_so_lowering_them_stops_the_sequence()
    {
        _service.AlwaysFail("x");
        await StartAsync();
        await _world.Runner.WhenIdleAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(30));
        _world.Data.RunsOf("job").Count().ShouldBe(2);

        _world.Data.Store("job", Settings(Schedule.Daily(T(15, 50)), retries: 1)); // the owner lowered the retries
        await AdvanceAsync(TimeSpan.FromMinutes(2));

        _world.Data.RunsOf("job").Count().ShouldBe(3); // the retry planned before the edit still runs; its failure (attempt 3 > 1 retry) ends the sequence
        await AdvanceAsync(TimeSpan.FromHours(3));
        _world.Data.RunsOf("job").Count().ShouldBe(3);
    }

    // ---- timeout, failures, stopping ----

    [Fact]
    public async Task A_run_past_its_timeout_is_cancelled_and_recorded_as_failed_timed_out()
    {
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(15, 50)), retries: 0, timeout: 5));
        var service = world.Services[0];
        service.Behavior = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return ServiceOutcome.Succeeded();
        };
        world.SetClock(At(12, 12, 0));
        await world.Runner.StartAsync("job", ServiceRunTrigger.Scheduled, "x", Ct);
        await service.WhenStartedAsync();

        world.Clock.Advance(TimeSpan.FromMinutes(4));
        world.Data.Runs.Single().IsRunning.ShouldBeTrue();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Runner.WhenIdleAsync();

        var run = world.Data.Runs.Single();
        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "Timed out after 5 min."));
    }

    [Fact]
    public async Task A_service_that_throws_fails_its_run_with_a_message_that_leaks_nothing()
    {
        _service.Behavior = (_, _) => throw new InvalidOperationException("password=hunter2 at 10.0.0.1");
        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        var run = _world.Data.RunsOf("job").First();

        run.Status.ShouldBe(ServiceRunStatus.Failed);
        run.Message.ShouldBe("The service failed unexpectedly. See the API log.");
    }

    [Fact]
    public async Task A_run_that_ends_when_the_application_stops_is_closed_as_interrupted_and_not_retried()
    {
        _service.Gate();
        await StartAsync();
        await _service.WhenStartedAsync();

        await _world.Runner.StopAsync(TimeSpan.FromSeconds(5));

        var run = _world.Data.RunsOf("job").ShouldHaveSingleItem();
        run.Status.ShouldBe(ServiceRunStatus.Failed);
        run.Message!.ShouldStartWith("Interrupted");
        _world.Runner.NextRetryAt.ShouldBeNull();
    }

    // ---- what a run is told, what it links ----

    [Fact]
    public async Task A_run_is_told_its_trigger_attempt_requester_and_the_parameters_with_defaults_filled_in()
    {
        var parameters = new[] { ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5) };
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(15, 50)), parameters: parameters));
        world.SetClock(At(12, 12, 0));

        await world.Runner.StartAsync("job", ServiceRunTrigger.CatchUp, "system:test", Ct);
        await world.Runner.WhenIdleAsync();

        var call = world.Services[0].Calls.ShouldHaveSingleItem();
        (call.Trigger, call.Attempt, call.RequestedBy).ShouldBe((ServiceRunTrigger.CatchUp, 1, "system:test"));
        call.Parameters["lookback"].GetInt32().ShouldBe(5);
        call.RunId.ShouldBe(world.Data.Runs.Single().Id);
    }

    [Fact]
    public async Task A_data_job_a_run_starts_is_linked_to_the_run_record()
    {
        var jobId = Guid.NewGuid();
        _service.Behavior = async (context, ct) =>
        {
            await context.LinkDataJobAsync(jobId, ct);
            return ServiceOutcome.Succeeded();
        };

        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        _world.Data.RunsOf("job").Single().DataJobId.ShouldBe(jobId);
    }

    [Fact]
    public async Task The_run_record_text_is_cut_to_one_line()
    {
        _service.AlwaysFail("line one\nline two " + new string('x', 700));
        await StartAsync();
        await _world.Runner.WhenIdleAsync();

        var message = _world.Data.RunsOf("job").First().Message!;

        message.ShouldNotContain('\n');
        message.Length.ShouldBe(ServiceRun.MaxMessageLength);
    }
}
