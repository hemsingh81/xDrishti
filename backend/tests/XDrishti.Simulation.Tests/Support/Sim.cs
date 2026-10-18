using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>Builds and runs a <see cref="Simulator"/> in one line: the helpers every test of the <c>Running</c> namespace uses.</summary>
internal static class Sim
{
    /// <summary>The simulator of the definition; the test fails with the located problems when there is none.</summary>
    public static Simulator Create(StrategyDefinition definition, Timeframe timeframe = Timeframe.M15)
    {
        var build = Simulator.Create(definition, timeframe);
        build.Simulator.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        return build.Simulator;
    }

    /// <summary>One run; the test fails with the error when it fails.</summary>
    public static SimulationResult Run(Simulator simulator, IReadOnlyList<Bar> bars, InstrumentRef? instrument = null, TradingCalendar? calendar = null)
    {
        var result = simulator.Run(instrument ?? Scan.Instrument, bars, calendar);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    /// <summary>Creates the simulator and runs it once.</summary>
    public static SimulationResult Run(StrategyDefinition definition, IReadOnlyList<Bar> bars, Timeframe timeframe = Timeframe.M15, InstrumentRef? instrument = null, TradingCalendar? calendar = null) =>
        Run(Create(definition, timeframe), bars, instrument, calendar);
}
