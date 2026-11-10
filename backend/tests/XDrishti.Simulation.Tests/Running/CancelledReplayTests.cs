using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// AU-12 carry-over: a replay of more than <c>Simulator.CancelMask + 1</c> (4096) minutes is abandoned when the token is cancelled, in both the single simulator and the comparison, so a chunk that ran past its time limit frees its worker.
/// </summary>
public sealed class CancelledReplayTests
{
    private const int Minutes = 5000;

    private static Bar[] ManyBars() =>
        [.. Enumerable.Range(0, Minutes).Select(i => new Bar(Day.Epoch(Day.Monday, 555) + (i * 60L), 100, 100.5, 99.5, 100, 1000))];

    [Fact]
    public void Simulator_RunUntilCancelled_throws_OperationCanceledException_on_a_cancelled_token_with_more_than_4096_bars()
    {
        var simulator = Simulator.Create(OrbV1.Create(), Timeframe.M15).Simulator.ShouldNotBeNull();
        var bars = ManyBars();
        bars.Length.ShouldBeGreaterThan(4096);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => simulator.RunUntilCancelled(Cand.Test, bars, Sim.Tick, null, null, null, null, null, null, null, cancelled.Token));
    }

    [Fact]
    public void SimulatorComparison_RunUntilCancelled_throws_OperationCanceledException_on_a_cancelled_token_with_more_than_4096_bars()
    {
        var comparison = Compared.Comparison(Compared.Choice(null, Compared.Seven));
        var bars = ManyBars();
        bars.Length.ShouldBeGreaterThan(4096);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => comparison.RunUntilCancelled(Cand.Test, bars, Sim.Tick, null, null, null, null, null, null, null, cancelled.Token));
    }

    [Fact]
    public void A_token_that_is_not_cancelled_runs_the_same_bars_to_the_end()
    {
        var simulator = Simulator.Create(OrbV1.Create(), Timeframe.M15).Simulator.ShouldNotBeNull();

        simulator.RunUntilCancelled(Cand.Test, ManyBars(), Sim.Tick, null, null, null, null, null, null, null, CancellationToken.None).IsSuccess.ShouldBeTrue();
    }
}
