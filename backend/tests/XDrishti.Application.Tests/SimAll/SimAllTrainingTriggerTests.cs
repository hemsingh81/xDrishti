using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Learning.SimTraining;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.SimAll;

/// <summary>AU-13: training runs after a job completes, only when the owner turned it on, once per host start, and never while the yield rules say wait.</summary>
public sealed class SimAllTrainingTriggerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeSimAllStore _store = new();
    private readonly ICommandHandler<TrainFromSimulationCommand, SimTrainingView> _training = Substitute.For<ICommandHandler<TrainFromSimulationCommand, SimTrainingView>>();
    private readonly FakeDisk _disk = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero)); // 20:00 IST, outside the session window

    private SimAllRunner Runner(bool enabled, string state, ICommandHandler<TrainFromSimulationCommand, SimTrainingView>? handler = null)
    {
        _store.Seed(1, 1, state);
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var scopes = new FakeScopes((typeof(ISimAllStore), _store), (typeof(ITestLedger), new FakeLedger()), (typeof(ISimAllBackfillGate), new FakeGate()), (typeof(IFreeDiskProbe), _disk), (typeof(ITradingCalendar), calendar),
            (typeof(SimTrainingSettings), new SimTrainingSettings { Enabled = enabled }), (typeof(ICommandHandler<TrainFromSimulationCommand, SimTrainingView>), handler ?? _training));
        return new SimAllRunner(scopes, new SimAllSettings(), _time, NullLogger<SimAllRunner>.Instance);
    }

    [Fact]
    public async Task A_completed_job_is_trained_on_once_when_training_is_on()
    {
        var runner = Runner(true, SimAllStates.Completed);

        await runner.TickAsync(Ct);
        await runner.TickAsync(Ct);

        await _training.Received(1).HandleAsync(Arg.Is<TrainFromSimulationCommand>(c => c.JobId == _store.Job!.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_trains_when_the_setting_is_off_the_job_was_cancelled_or_the_yield_rules_say_wait()
    {
        await Runner(false, SimAllStates.Completed).TickAsync(Ct);
        await Runner(true, SimAllStates.Cancelled).TickAsync(Ct);
        _disk.Free = 1;
        await Runner(true, SimAllStates.CompletedWithFailures).TickAsync(Ct);

        await _training.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_failing_training_does_not_stop_the_loop_and_is_not_retried_until_the_next_start()
    {
        var failing = new Failing();
        var runner = Runner(true, SimAllStates.CompletedWithFailures, failing);

        await runner.TickAsync(Ct);
        await runner.TickAsync(Ct);

        failing.Calls.ShouldBe(1);
    }

    private sealed class Failing : ICommandHandler<TrainFromSimulationCommand, SimTrainingView>
    {
        public int Calls { get; private set; }

        public ValueTask<Result<SimTrainingView>> HandleAsync(TrainFromSimulationCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }
    }
}
