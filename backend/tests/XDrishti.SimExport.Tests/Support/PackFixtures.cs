using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>
/// The pools of the verification-pack tests (spec P3-09): invented trades on invented symbols, put together by the real assembler. A <see cref="Neutral"/> trade belongs to no edge category (a buy that stops out at its stop, in a minute
/// after the entry, filled at the open, on a complete trigger bar, decided at the entry instant); a test makes it belong to one category by changing one member.
/// </summary>
internal static class PackFixtures
{
    public static readonly DateOnly First = new(2024, 4, 1);

    public static readonly DateOnly Last = new(2026, 4, 30);

    public static Guid IdOf(string symbol) => new(SHA256.HashData(Encoding.UTF8.GetBytes(symbol))[..16]);

    /// <summary>A trade in no category: key <c>strategy|symbol|Buy|date</c>.</summary>
    public static SimTrade Neutral(string strategy, string symbol, DateOnly date, Side side = Side.Buy)
    {
        var stop = side == Side.Buy ? 98d : 103d;
        var trade = FabricatedRun.Trade(IdOf(symbol), symbol, side, date, ExitReason.Stop, entryMinute: 600, entry: 100.5, stop: stop, target: 105.5, exitPrice: stop, rExact: -1, rFilled: -1.05, strategy: strategy);
        return trade with { DecisionEpochSeconds = trade.EntryEpochSeconds };
    }

    /// <summary>A run of one strategy over the trades; the instruments are the symbols of the trades.</summary>
    public static SimulationRun Run(string strategy, IReadOnlyList<SimTrade> trades, IReadOnlyList<SimTrade>? open = null, Func<InstrumentRun, InstrumentRun>? instrument = null, string? definition = null)
    {
        var symbols = trades.Concat(open ?? []).Select(t => t.Symbol).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var instruments = symbols.Select(s => (instrument ?? (i => i))(FabricatedRun.Ran(IdOf(s), s, First, Last))).ToList();
        var run = FabricatedRun.Assemble(FabricatedRun.List([.. symbols.Select(s => (IdOf(s), s))]), instruments, trades, open, from: First, to: Last, strategy: strategy);
        return definition is null ? run : run with { Strategy = run.Strategy with { DefinitionJson = definition } };
    }

    /// <summary>The pool of the hand-calculated draw: strategy X over A and B in 2024 and 2025, strategy Y over A in 2024 and 2025, two trades in each cell, and one trade of X that the data cut short.</summary>
    public static IReadOnlyList<SimulationRun> HandPool()
    {
        SimTrade[] Cell(string strategy, string symbol, int year) => [Neutral(strategy, symbol, new DateOnly(year, 4, 4)), Neutral(strategy, symbol, new DateOnly(year, 4, 5))];
        var x = new[] { Cell("X", "NSE:A", 2024), Cell("X", "NSE:A", 2025), Cell("X", "NSE:B", 2024), Cell("X", "NSE:B", 2025) }.SelectMany(c => c).ToList();
        var y = new[] { Cell("Y", "NSE:A", 2024), Cell("Y", "NSE:A", 2025) }.SelectMany(c => c).ToList();
        return [Run("X", x, [Neutral("X", "NSE:A", new DateOnly(2025, 4, 6))]), Run("Y", y)];
    }

    public static IReadOnlyList<string> Keys(IEnumerable<SampledTrade> picks) => [.. picks.Select(p => p.Trade.Trade.Key)];
}
