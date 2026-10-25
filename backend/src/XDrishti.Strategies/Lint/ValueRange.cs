using System.Collections.Frozen;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// The values a catalog feature can take by its definition (spec P3-02f, decision D5): a closed, conservative table used by <see cref="NeverTrueRule"/>. Only a bound that the definition guarantees is listed; a feature
/// without a row is never warned about. <see cref="Minimum"/> / <see cref="Maximum"/> are infinite for "no bound"; <see cref="MinimumIncluded"/> is false for a bound that is approached and not reached.
/// </summary>
internal readonly record struct ValueRange(double Minimum, double Maximum, bool MinimumIncluded = true)
{
    /// <summary>The table, by catalog name. A test compares the names with <c>IndicatorCatalog</c>.</summary>
    public static readonly FrozenDictionary<string, ValueRange> Table = BuildTable();

    private static FrozenDictionary<string, ValueRange> BuildTable()
    {
        var table = new Dictionary<string, ValueRange>(StringComparer.Ordinal);
        foreach (var name in new[] { "rsi", "plus_di", "minus_di", "adx", "index_vol_pct", "vix_pct", "breadth" })
        {
            table[name] = new ValueRange(0, 100);
        }

        table["regime_vol"] = new ValueRange(0, 2);
        foreach (var name in new[] { "supertrend_dir", "engulfing" })
        {
            table[name] = new ValueRange(-1, 1);
        }

        foreach (var name in new[] { "inside_bar", "nr", "is_expiry_day" })
        {
            table[name] = new ValueRange(0, 1);
        }

        table["day_of_week"] = new ValueRange(1, 7);
        foreach (var name in new[] { "atr", "volume", "rel_volume", "prev_day_volume", "bb_width" })
        {
            table[name] = new ValueRange(0, double.PositiveInfinity);
        }

        table["gap_pct"] = new ValueRange(-100, double.PositiveInfinity, MinimumIncluded: false);
        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
