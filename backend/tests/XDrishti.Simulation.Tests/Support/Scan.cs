using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>Compiles and scans in one line: the helpers every scanner test uses.</summary>
internal static class Scan
{
    public static readonly InstrumentRef Instrument = new(new Guid("00000000-0000-0000-0000-000000000001"), "TEST");

    /// <summary>The compiled side; the test fails with the located problems when it does not compile.</summary>
    public static CompiledSide Compile(StrategyDefinition definition, Side side, Timeframe timeframe = Timeframe.M15)
    {
        var result = StrategyCompiler.Compile(definition, timeframe, side);
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        return result.Compiled!;
    }

    public static EntryScanner Scanner(CompiledSide side, TradingCalendar? calendar = null)
    {
        var created = EntryScanner.Create(side, Instrument, calendar);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code + ": " + created.Error.Message : string.Empty);
        return created.Value;
    }

    /// <summary>Steps every bar through a new scanner of the side.</summary>
    public static EntryScanner Run(CompiledSide side, IEnumerable<Bar> bars, TradingCalendar? calendar = null)
    {
        var scanner = Scanner(side, calendar);
        foreach (var bar in bars)
        {
            scanner.Step(bar);
        }

        return scanner;
    }

    public static EntryScanner Run(StrategyDefinition definition, Side side, IEnumerable<Bar> bars, Timeframe timeframe = Timeframe.M15, TradingCalendar? calendar = null) =>
        Run(Compile(definition, side, timeframe), bars, calendar);

    /// <summary>The scanners of the sides the definition lists (Buy, then Sell), each run over the bars.</summary>
    public static IReadOnlyList<EntryScanner> Both(StrategyDefinition definition, IReadOnlyList<Bar> bars, Timeframe timeframe = Timeframe.M15, TradingCalendar? calendar = null) =>
        [.. definition.Sides.OrderBy(s => s).Select(s => Run(definition, s, bars, timeframe, calendar))];
}
