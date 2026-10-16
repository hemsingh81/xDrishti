namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// The 56 names of the catalog written in their DSL form with the default arguments, as doc 11 §2 lists them (a feature with parameters is called, <c>rsi()</c>; one without is a bare name, <c>close</c>; the dotted
/// spellings for the context names the DSL abbreviates; <c>regime()</c> for the regime). Written out here by hand and checked against the catalog by a test, so a name added to the catalog without a form fails.
/// </summary>
internal static class AllFeatureForms
{
    public static readonly IReadOnlyDictionary<string, string> Forms = new Dictionary<string, string>
    {
        ["sma"] = "sma()",
        ["ema"] = "ema()",
        ["rsi"] = "rsi()",
        ["atr"] = "atr()",
        ["highest"] = "highest()",
        ["lowest"] = "lowest()",
        ["vwap"] = "vwap",
        ["rel_volume"] = "rel_volume()",
        ["or_high"] = "or_high()",
        ["or_low"] = "or_low()",
        ["adx"] = "adx()",
        ["plus_di"] = "plus_di()",
        ["minus_di"] = "minus_di()",
        ["macd_line"] = "macd_line()",
        ["macd_signal"] = "macd_signal()",
        ["macd_hist"] = "macd_hist()",
        ["bb_upper"] = "bb_upper()",
        ["bb_middle"] = "bb_middle()",
        ["bb_lower"] = "bb_lower()",
        ["bb_width"] = "bb_width()",
        ["supertrend"] = "supertrend()",
        ["supertrend_dir"] = "supertrend_dir()",
        ["roc"] = "roc()",
        ["inside_bar"] = "inside_bar",
        ["nr"] = "nr()",
        ["engulfing"] = "engulfing",
        ["pivot_high"] = "pivot_high()",
        ["pivot_low"] = "pivot_low()",
        ["pivot_high_age"] = "pivot_high_age()",
        ["pivot_low_age"] = "pivot_low_age()",
        ["open"] = "open",
        ["high"] = "high",
        ["low"] = "low",
        ["close"] = "close",
        ["volume"] = "volume",
        ["day_of_week"] = "day_of_week",
        ["is_expiry_day"] = "is_expiry_day",
        ["index_trend"] = "index.trend",
        ["index_adx"] = "index.adx",
        ["index_vol_pct"] = "index_vol_pct",
        ["vix"] = "vix",
        ["vix_pct"] = "vix_pct",
        ["vix_chg"] = "vix_chg",
        ["breadth"] = "breadth",
        ["regime_trend"] = "regime_trend",
        ["regime_vol"] = "regime_vol",
        ["regime"] = "regime()",
        ["prev_day_open"] = "prev_day.open",
        ["prev_day_high"] = "prev_day.high",
        ["prev_day_low"] = "prev_day.low",
        ["prev_day_close"] = "prev_day.close",
        ["prev_day_volume"] = "prev_day.volume",
        ["today_open"] = "today.open",
        ["today_high"] = "today.high",
        ["today_low"] = "today.low",
        ["gap_pct"] = "gap_pct",
    };

    /// <summary>A whole condition that reads the form: a comparison with 0 (the regime, which is compared with a name or a code, with 1).</summary>
    public static string ConditionOf(string name) => name == "regime" ? "regime() == 1" : Forms[name] + " > 0";
}
