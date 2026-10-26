#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Learning.Jobs;

/// <summary>Spec P8-18 Part B, criteria 1, 3, 4 and 5 behind the ports: the order of the refusals (capability, owner and input, baseline, rebuild proof, knob rules, one at a time, daily limit), the identical input returning its job, one audit event per real start, and nothing read or written while the capability is off.</summary>
public sealed class StartExperimentJobHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ICapabilityGate _gate = Substitute.For<ICapabilityGate>();

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unit = Substitute.For<IAccountUnitOfWork>();

    private readonly JobFixtures.FakeJobs _jobs = new();

    private readonly ExperimentJobSettings _settings = new();

    public StartExperimentJobHandlerTests()
    {
        _gate.RequireAsync("experiments_ui", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success()));
        _runs.FindAsync(JobFixtures.BaselineKey, Arg.Any<CancellationToken>()).Returns(JobFixtures.Header());
        _runs.ReadRecordTextAsync(JobFixtures.BaselineKey, Arg.Any<CancellationToken>()).Returns(JobFixtures.Record());
    }

    private StartExperimentJobHandler Handler(Func<ExitChoice?, bool>? reproduces = null) =>
        new(_gate, _runs, new ReplayPlanner(JobFixtures.Engine(reproduces ?? (exits => exits is null)), JobFixtures.Source(null)), _jobs, _events, _unit, _settings, new FixedClock(Now), NullLogger<StartExperimentJobHandler>.Instance);

    private ValueTask<Result<StartedExperimentJob>> Start(string kind = "exit", string name = "FIXED_RR.rr", double? value = 3, string actor = JobFixtures.Owner, string reason = "try a wider target", string key = JobFixtures.BaselineKey, int? holdOut = null) =>
        Handler().HandleAsync(new StartExperimentJobCommand(key, kind, name, value, holdOut, reason, actor), Ct);

    [Fact]
    public async Task While_the_capability_is_off_nothing_is_read_and_nothing_is_written()
    {
        _gate.RequireAsync("experiments_ui", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure(XDrishti.Domain.Capabilities.CapabilityErrors.Off("experiments_ui"))));

        var result = await Start();

        result.Error.Code.ShouldBe("capabilities.off");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        _jobs.Rows.ShouldBeEmpty();
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
        _events.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task A_capability_switched_off_between_the_check_and_the_insert_is_refused_by_the_database_and_answered_as_off_not_as_a_failure()
    {
        _jobs.RefuseAsOff = true;

        var result = await Start();

        result.Error.Code.ShouldBe("capabilities.off");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        _events.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task A_valid_start_queues_one_job_with_the_owner_the_knob_and_the_instrument_count_and_audits_it_once_without_the_value()
    {
        var result = await Start();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Created.ShouldBeTrue();
        var job = result.Value.Job;
        job.State.ShouldBe("queued");
        job.Knob.ShouldBe(new ExperimentKnobView("exit", "FIXED_RR.rr", "2", "3"));
        job.Instruments.ShouldBe(2);
        job.Limits.ShouldContain("Nothing was changed. Applying a change is your own separate act.");
        _jobs.Rows.Single().CreatedBy.ShouldBe(JobFixtures.Owner);
        _jobs.Rows.Single().HoldOutMonths.ShouldBe(6);
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.ExperimentJobStarted && e.Detail == "kind=exit instruments=2" && e.Actor == JobFixtures.Owner));
        await _unit.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("cli:xd")]
    [InlineData("")]
    [InlineData("rule:autonomy")]
    public async Task Only_an_owner_actor_starts_a_job(string actor)
    {
        var result = await Start(actor: actor);

        result.Error.Code.ShouldBe("experiments.owner_only");
        _jobs.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bad_input_is_refused_before_a_run_is_read()
    {
        (await Start(key: "nope")).Error.Code.ShouldBe("learning.invalid");
        (await Start(reason: " ")).Error.Code.ShouldBe("learning.invalid");
        (await Start(value: null)).Error.Code.ShouldBe("learning.invalid");
        (await Start(value: double.NaN)).Error.Code.ShouldBe("learning.invalid");
        (await Start(holdOut: 121)).Error.Code.ShouldBe("learning.invalid");

        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
        _jobs.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_baseline_that_is_not_stored_is_not_found_and_one_that_cannot_be_rebuilt_is_refused_with_the_reason()
    {
        _runs.FindAsync(JobFixtures.BaselineKey, Arg.Any<CancellationToken>()).Returns((StoredRunHeader?)null);
        (await Start()).Error.Code.ShouldBe("learning.run_not_found");
        _runs.FindAsync(JobFixtures.BaselineKey, Arg.Any<CancellationToken>()).Returns(JobFixtures.Header());

        var notRebuildable = await Handler(_ => false).HandleAsync(new StartExperimentJobCommand(JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", 3, null, "why", JobFixtures.Owner), Ct);

        notRebuildable.Error.Code.ShouldBe("experiments.baseline_not_rebuildable");
        _jobs.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("parameter", "or_min", 10)]
    [InlineData("exit", "FIXED_RR.rr", 2)]
    [InlineData("exit", "FIXED_RR.rr", 11)]
    [InlineData("slippage", "slippage.ticks", 0.5)]
    public async Task A_knob_that_is_not_allowed_is_refused_before_anything_is_queued(string kind, string name, double value)
    {
        var result = await Start(kind, name, value);

        result.Error.Code.ShouldBe("experiments.knob_not_allowed");
        _jobs.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_identical_input_returns_the_same_job_without_a_second_row_or_a_second_audit_event()
    {
        var first = await Start();
        var again = await Start();

        again.Value.Created.ShouldBeFalse();
        again.Value.Job.Id.ShouldBe(first.Value.Job.Id);
        _jobs.Rows.Count.ShouldBe(1);
        _events.ReceivedWithAnyArgs(1).Record(default!);
    }

    [Fact]
    public async Task While_a_job_is_queued_or_running_another_input_is_refused_naming_the_active_job()
    {
        var first = await Start();

        var second = await Start(value: 4);

        second.Error.Code.ShouldBe("experiments.job_active");
        second.Error.Kind.ShouldBe(ErrorKind.Conflict);
        second.Error.Message.ShouldContain(first.Value.Job.Id.ToString("D"));
        _jobs.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_third_job_in_24_hours_is_allowed_the_fourth_is_refused_with_the_time_the_oldest_leaves_and_failed_and_cancelled_count()
    {
        foreach (var value in new double[] { 3, 4, 5 })
        {
            var started = await Start(value: value);
            started.IsSuccess.ShouldBeTrue();
            _jobs.Rows[^1] = _jobs.Rows[^1] with { State = value == 3 ? ExperimentJobState.Failed : ExperimentJobState.Cancelled, CreatedAt = Now.AddHours(-value) };
        }

        var fourth = await Start(value: 6);

        fourth.Error.Code.ShouldBe("experiments.daily_limit");
        fourth.Error.Kind.ShouldBe(ErrorKind.RateLimited);
        fourth.Error.RetryAfterSeconds.ShouldBe((int)TimeSpan.FromHours(24 - 5).TotalSeconds);
        _jobs.Rows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_lowered_daily_limit_is_honoured_and_the_window_moves_with_the_clock()
    {
        _settings.MaxJobsPerDay = 1;
        (await Start()).IsSuccess.ShouldBeTrue();
        _jobs.Rows[0] = _jobs.Rows[0] with { State = ExperimentJobState.Failed, CreatedAt = Now.AddHours(-25) };

        (await Start(value: 4)).IsSuccess.ShouldBeTrue(); // the first left the window
        _jobs.Rows[^1] = _jobs.Rows[^1] with { State = ExperimentJobState.Cancelled };

        (await Start(value: 5)).Error.Code.ShouldBe("experiments.daily_limit");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
