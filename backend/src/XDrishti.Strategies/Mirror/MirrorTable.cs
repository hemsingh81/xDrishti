using System.Collections.Frozen;

namespace XDrishti.Strategies.Mirror;

/// <summary>
/// The closed table of the BUY to SELL mirror (spec P3-02e, ADR 0106): one row for every catalog name, the builtins and the constants, either with a mirror type and the name it mirrors to, or refused. Any change to a row
/// or a refusal bumps <see cref="Version"/>; a test pins <see cref="Fingerprint"/> so that a silent edit fails, and another fails when a catalog name has no row.
/// </summary>
public static class MirrorTable
{
    /// <summary>The version of the table; it is part of the canonical definition of a strategy whose SELL was mirrored, so a new version is a new run key.</summary>
    public const string Version = "mirror-1";

    private static readonly FrozenDictionary<string, MirrorRow> Rows = Build();

    private static readonly FrozenSet<string> Refused = new[]
    {
        "day_of_week", "is_expiry_day", "index_trend", "index_adx", "index_vol_pct", "vix", "vix_pct", "vix_chg", "breadth", "regime_trend", "regime_vol", "regime",
        "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The builtin functions the mirror rewrites (<c>regime</c> is refused).</summary>
    internal static readonly FrozenSet<string> Builtins = new[] { "crosses_above", "crosses_below", "rising", "falling", "between", "abs", "min", "max" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Every name that has a row, typed or refused (catalog names, constants), sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Rows.Keys.Concat(Refused).Order(StringComparer.Ordinal)];

    /// <summary>The row of a catalog name; false for a refused name and for a name the table does not know.</summary>
    internal static bool TryGet(string name, out MirrorRow row) => Rows.TryGetValue(name, out row!);

    /// <summary>Whether the name is refused (market context, calendar, constants).</summary>
    public static bool IsRefused(string name) => Refused.Contains(name);

    /// <summary>The whole table as text, one row per line, sorted: what the pin test hashes.</summary>
    public static string Fingerprint() =>
        string.Join('\n', Rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.Key + "|" + r.Value.Kind + "|" + r.Value.Mirrored + "|" + r.Value.Exact)
            .Concat(Refused.Order(StringComparer.Ordinal).Select(r => r + "|refused"))
            .Concat(Builtins.Order(StringComparer.Ordinal).Select(b => b + "|builtin"))
            .Append(Version));

    private static FrozenDictionary<string, MirrorRow> Build()
    {
        var rows = new Dictionary<string, MirrorRow>(StringComparer.Ordinal);
        foreach (var name in new[] { "open", "close", "vwap", "sma", "ema", "bb_middle", "prev_day_open", "prev_day_close", "today_open" })
        {
            rows[name] = new MirrorRow(MirrorKind.Level, name);
        }

        foreach (var (a, b) in new[]
        {
            ("high", "low"), ("bb_upper", "bb_lower"), ("highest", "lowest"), ("pivot_high", "pivot_low"), ("or_high", "or_low"), ("prev_day_high", "prev_day_low"), ("today_high", "today_low"),
        })
        {
            rows[a] = new MirrorRow(MirrorKind.Level, b);
            rows[b] = new MirrorRow(MirrorKind.Level, a);
        }

        foreach (var name in new[] { "macd_line", "macd_signal", "macd_hist", "engulfing" })
        {
            rows[name] = new MirrorRow(MirrorKind.Signed, name);
        }

        foreach (var name in new[] { "roc", "gap_pct" })
        {
            rows[name] = new MirrorRow(MirrorKind.Signed, name, false);
        }

        // not exact (found by the metamorphic test): the indicator starts in an up-trend whatever the market does, so it is not symmetric until its first flip; it mirrors by meaning
        rows["supertrend"] = new MirrorRow(MirrorKind.Level, "supertrend", false);
        rows["supertrend_dir"] = new MirrorRow(MirrorKind.Signed, "supertrend_dir", false);
        rows["rsi"] = new MirrorRow(MirrorKind.Osc, "rsi");
        foreach (var name in new[] { "atr", "adx", "rel_volume", "volume", "prev_day_volume", "inside_bar", "nr" })
        {
            rows[name] = new MirrorRow(MirrorKind.Plain, name);
        }

        rows["bb_width"] = new MirrorRow(MirrorKind.Plain, "bb_width", false);
        foreach (var (a, b) in new[] { ("pivot_high_age", "pivot_low_age"), ("plus_di", "minus_di") })
        {
            rows[a] = new MirrorRow(MirrorKind.Plain, b);
            rows[b] = new MirrorRow(MirrorKind.Plain, a);
        }

        return rows.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
