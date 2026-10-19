using System.Reflection;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The comparator (spec P3-08 D4, D5): every field of the oracle's trade and every counter, the no-trade reason and the hold end. Exact equality for prices, money, epochs, enums and counts (a double is compared bit for bit, so a
/// <c>-0.0</c> against a <c>+0.0</c> is a difference); for the five ratio fields exact first, then the tolerance of <see cref="RatioStats.Tolerance"/>, with the largest difference recorded. It reads the properties by reflection, so a
/// field added to the oracle's records is compared without a line of code here (and the field-coverage test names it).
/// </summary>
internal static class OracleComparer
{
    /// <summary>The five fields whose operations after the first the rule tables do not fix (D5).</summary>
    internal static readonly IReadOnlySet<string> RatioFields = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(OracleTrade.RExact), nameof(OracleTrade.RFilled), nameof(OracleTrade.SlippageR), nameof(OracleTrade.MaeR), nameof(OracleTrade.MfeR),
    };

    private static readonly PropertyInfo[] TradeProperties = typeof(OracleTrade).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToArray();

    private static readonly PropertyInfo[] CounterProperties = typeof(OracleCounters).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToArray();

    /// <summary>The names of the compared trade fields and counters (what the field-coverage test reads).</summary>
    internal static IReadOnlyList<string> TradeFields { get; } = [.. TradeProperties.Select(p => p.Name)];

    internal static IReadOnlyList<string> CounterFields { get; } = [.. CounterProperties.Select(p => p.Name)];

    /// <summary>Every difference between the oracle's result and the engine's (mapped into the oracle's records), under the rules of D5.</summary>
    internal static List<Mismatch> Diff(OracleResult oracle, OracleResult engine, RatioStats stats, string where)
    {
        var found = new List<Mismatch>();
        if (oracle.NoTrade != engine.NoTrade)
        {
            found.Add(new Mismatch("NoTrade", OracleAdapter.Show(oracle.NoTrade), OracleAdapter.Show(engine.NoTrade)));
        }

        if (oracle.HoldEndEpochSeconds != engine.HoldEndEpochSeconds)
        {
            found.Add(new Mismatch("HoldEndEpochSeconds", OracleAdapter.Show(oracle.HoldEndEpochSeconds), OracleAdapter.Show(engine.HoldEndEpochSeconds)));
        }

        if ((oracle.Trade is null) != (engine.Trade is null))
        {
            found.Add(new Mismatch("Trade", oracle.Trade is null ? "none" : "a trade", engine.Trade is null ? "none" : "a trade"));
        }
        else if (oracle.Trade is not null && engine.Trade is not null)
        {
            foreach (var property in TradeProperties)
            {
                CompareValue(property, property.GetValue(oracle.Trade), property.GetValue(engine.Trade), stats, where, found);
            }
        }

        foreach (var property in CounterProperties)
        {
            CompareValue(property, property.GetValue(oracle.Counters), property.GetValue(engine.Counters), stats, where, found);
        }

        return found;
    }

    private static void CompareValue(PropertyInfo property, object? oracle, object? engine, RatioStats stats, string where, List<Mismatch> found)
    {
        if (property.Name is nameof(OracleTrade.RExact) or nameof(OracleTrade.RFilled) or nameof(OracleTrade.SlippageR) or nameof(OracleTrade.MaeR) or nameof(OracleTrade.MfeR))
        {
            if (!stats.Accept(property.Name, (double)oracle!, (double)engine!, where))
            {
                found.Add(new Mismatch(property.Name, OracleAdapter.Show(oracle), OracleAdapter.Show(engine)));
            }

            return;
        }

        if (!SameValue(oracle, engine))
        {
            found.Add(new Mismatch(property.Name, OracleAdapter.Show(oracle), OracleAdapter.Show(engine)));
        }
    }

    /// <summary>Equality for everything but the ratio fields: a double is compared by its bits (so the sign of a zero counts), the rest by <c>Equals</c>.</summary>
    private static bool SameValue(object? oracle, object? engine) => (oracle, engine) switch
    {
        (double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b),
        _ => Equals(oracle, engine),
    };
}
