namespace XDrishti.Domain.Learning;

/// <summary>
/// The decision-time features of catalog version <see cref="Version"/> (P6-04, ADR 0066), in a fixed order that a test pins. The first <see cref="StoredCount"/> are extracted once per trade and stored (<c>research.trade_features</c>); the last
/// <see cref="CellCount"/> are computed when a training set is built because they depend on which runs feed the cell statistics. Every value is a number; unknown is <see cref="double.NaN"/>. Changing a name, the order or the meaning raises the version.
/// </summary>
public static class TradeFeatureCatalog
{
    /// <summary>The catalog version written next to every stored row and every training set.</summary>
    public const string Version = "tf-1";

    /// <summary>The number of features extracted and stored per trade (everything but the cell group).</summary>
    public const int StoredCount = 36;

    /// <summary>The number of cell-statistics features appended at build time.</summary>
    public const int CellCount = 5;

    private static readonly TradeFeatureDef[] Defs =
    [
        new("rsi14", TradeFeatureSource.Bars, true),
        new("atr_pct14", TradeFeatureSource.Bars, true),
        new("adx14", TradeFeatureSource.Bars, true),
        new("plus_di14", TradeFeatureSource.Bars, true),
        new("minus_di14", TradeFeatureSource.Bars, true),
        new("ema20_dist_pct", TradeFeatureSource.Bars, true),
        new("ema50_dist_pct", TradeFeatureSource.Bars, true),
        new("macd_hist_pct", TradeFeatureSource.Bars, true),
        new("bb_width20", TradeFeatureSource.Bars, true),
        new("bb_pos20", TradeFeatureSource.Bars, true),
        new("roc10", TradeFeatureSource.Bars, true),
        new("rel_volume20", TradeFeatureSource.Bars, true),
        new("vwap_dist_pct", TradeFeatureSource.Bars, true),
        new("range_pos20", TradeFeatureSource.Bars, true),
        new("supertrend_dir", TradeFeatureSource.Bars, true),
        new("gap_pct", TradeFeatureSource.Bars, true),
        new("prev_close_dist_pct", TradeFeatureSource.Bars, true),
        new("day_range_pos", TradeFeatureSource.Bars, true),
        new("prev_day_range_pct", TradeFeatureSource.Bars, true),
        new("day_open_dist_pct", TradeFeatureSource.Bars, true),
        new("bar_range_pct", TradeFeatureSource.Bars, true),
        new("bar_body_pct", TradeFeatureSource.Bars, true),
        new("day_of_week", TradeFeatureSource.Bars, true),
        new("is_expiry_day", TradeFeatureSource.Context, false),
        new("index_trend", TradeFeatureSource.Context, false),
        new("index_adx", TradeFeatureSource.Context, false),
        new("index_vol_pct", TradeFeatureSource.Context, false),
        new("vix", TradeFeatureSource.Context, false),
        new("vix_pct", TradeFeatureSource.Context, false),
        new("vix_chg", TradeFeatureSource.Context, false),
        new("breadth", TradeFeatureSource.Context, false),
        new("regime", TradeFeatureSource.Context, false),
        new("minute_of_day", TradeFeatureSource.Plan, true),
        new("side_sign", TradeFeatureSource.Plan, true),
        new("risk_pct", TradeFeatureSource.Plan, true),
        new("planned_rr", TradeFeatureSource.Plan, true),
        new("cell_n", TradeFeatureSource.Cell, true),
        new("cell_win_posterior", TradeFeatureSource.Cell, true),
        new("cell_win_lower", TradeFeatureSource.Cell, true),
        new("cell_expectancy_r", TradeFeatureSource.Cell, false),
        new("cell_enough", TradeFeatureSource.Cell, true),
    ];

    /// <summary>All features, stored ones first.</summary>
    public static IReadOnlyList<TradeFeatureDef> All => Defs;

    /// <summary>The names in order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Defs.Select(d => d.Name)];

    /// <summary>The names a row must have known by default: the bar, plan and cell features that are always defined once warmed up. Context features (not loaded yet) and the cell expectancy (unknown for an empty cell) are not.</summary>
    public static IReadOnlyList<string> DefaultRequired { get; } = [.. Defs.Where(d => d.RequiredByDefault).Select(d => d.Name)];

    /// <summary>The position of a name, or -1.</summary>
    public static int IndexOf(string name)
    {
        for (var i = 0; i < Defs.Length; i++)
        {
            if (string.Equals(Defs[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
