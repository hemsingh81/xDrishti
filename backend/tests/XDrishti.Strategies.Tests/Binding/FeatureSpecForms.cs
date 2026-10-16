using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using static XDrishti.Strategies.Tests.Support.Specs;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>What a form must bind to: the strategy timeframe and parameters it is compiled with, and the specs it reads, in order.</summary>
public sealed record SpecCase(Timeframe Evaluation, IReadOnlyDictionary<string, double> Parameters, FeatureSpec[] Features);

/// <summary>
/// The forms and the specs written out by hand from the rules of the spec: positional arguments in catalog order, the catalog's defaults for the rest, the named <c>source=</c>, the <c>tf()</c> qualifier, the dotted
/// spellings, strategy parameters, and the calendar and context names (always read on 1m). A source that is the key appears once; the strategy timeframe is 15m unless the case says otherwise.
/// </summary>
public static class FeatureSpecForms
{
    private static readonly IReadOnlyDictionary<string, double> None = new Dictionary<string, double>();

    private const Timeframe M15 = Timeframe.M15;

    private static SpecCase At(Timeframe timeframe, params FeatureSpec[] features) => new(timeframe, None, features);

    private static SpecCase On15(params FeatureSpec[] features) => At(M15, features);

    private static SpecCase With(IReadOnlyDictionary<string, double> parameters, params FeatureSpec[] features) => new(M15, parameters, features);

    public static readonly IReadOnlyDictionary<string, SpecCase> Cases = new Dictionary<string, SpecCase>
    {
        // positional arguments, defaults and the named source
        ["rsi(14) > 55"] = On15(Rsi(M15)),
        ["rsi() > 55"] = On15(Rsi(M15)),
        ["rsi(21) > 55"] = On15(Rsi(M15, 21)),
        ["rsi(14, source=close) > 55"] = On15(Rsi(M15)),
        ["rsi(source=high) > 55"] = On15(Rsi(M15, 14, PriceSource.High)),
        ["rsi(21, source=open) > 55"] = On15(Rsi(M15, 21, PriceSource.Open)),
        ["ema(20) > ema(50)"] = On15(Ema(M15), Ema(M15, 50)),
        ["ema() > 0"] = On15(Ema(M15)),
        ["sma(5, source=low) > 0"] = On15(Of("sma", M15, 5, source: PriceSource.Low)),
        ["highest(10) > 0"] = On15(Of("highest", M15, 10, source: PriceSource.High)),
        ["highest(10, source=high) > 0"] = On15(Of("highest", M15, 10, source: PriceSource.High)),
        ["highest(10, source=close) > 0"] = On15(Of("highest", M15, 10, source: PriceSource.Close)),
        ["lowest() > 0"] = On15(Of("lowest", M15, 20, source: PriceSource.Low)),
        ["atr() > 0"] = On15(Of("atr", M15, 14)),
        ["adx(10) > 20"] = On15(Of("adx", M15, 10)),
        ["plus_di() > minus_di()"] = On15(Of("plus_di", M15, 14), Of("minus_di", M15, 14)),
        ["roc(5) > 0"] = On15(Of("roc", M15, 5)),
        ["nr() > 0"] = On15(Of("nr", M15, 7)),
        ["pivot_high(3) > 0"] = On15(Of("pivot_high", M15, 3)),
        ["rel_volume() > 1"] = On15(Of("rel_volume", M15, 20)),
        ["or_high(30) > 0"] = On15(Of("or_high", M15, 30)),
        ["bb_upper(20, 2) > 0"] = On15(Of("bb_upper", M15, 20, 2)),
        ["bb_upper() > 0"] = On15(Of("bb_upper", M15, 20, 2)),
        ["bb_upper(30) > 0"] = On15(Of("bb_upper", M15, 30, 2)),
        ["bb_lower(20, 1.5) > 0"] = On15(Of("bb_lower", M15, 20, 1.5)),
        ["macd_line() > 0"] = On15(Of("macd_line", M15, 12, 26, 9)),
        ["macd_hist(5, 35, 5) > 0"] = On15(Of("macd_hist", M15, 5, 35, 5)),
        ["supertrend(7, 2) > 0"] = On15(Of("supertrend", M15, 7, 2)),

        // features without parameters
        ["close > 0"] = On15(Close(M15)),
        ["volume > 0"] = On15(Of("volume", M15)),
        ["vwap > 0"] = On15(Of("vwap", M15)),
        ["engulfing != 0"] = On15(Of("engulfing", M15)),
        ["gap_pct > 1"] = On15(Of("gap_pct", M15)),

        // the timeframe qualifier: all nine
        ["tf(\"1m\").close > 0"] = On15(Close(Timeframe.M1)),
        ["tf(\"5m\").rsi() > 0"] = On15(Rsi(Timeframe.M5)),
        ["tf(\"10m\").rsi() > 0"] = On15(Rsi(Timeframe.M10)),
        ["tf(\"15m\").rsi() > 0"] = On15(Rsi(M15)),
        ["tf(\"30m\").rsi() > 0"] = On15(Rsi(Timeframe.M30)),
        ["tf(\"1h\").ema(20) > 0"] = On15(Ema(Timeframe.H1)),
        ["tf(\"1D\").close > 0"] = On15(Close(Timeframe.D1)),
        ["tf(\"1W\").close > 0"] = On15(Close(Timeframe.W1)),
        ["tf(\"1M\").close > 0"] = On15(Close(Timeframe.Mo1)),
        ["tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"] = On15(Ema(Timeframe.H1), Ema(Timeframe.H1, 50)),
        ["tf(\"1D\").prev_day_high > 0"] = On15(Of("prev_day_high", Timeframe.D1)),
        ["tf(\"1m\").or_high(5) > 0"] = On15(Of("or_high", Timeframe.M1, 5)),

        // the strategy timeframe is the default
        ["rsi() > 0"] = At(Timeframe.H1, Rsi(Timeframe.H1)),
        ["rsi(14) < 70"] = At(Timeframe.D1, Rsi(Timeframe.D1)),
        ["close > tf(\"1W\").close"] = At(Timeframe.W1, Close(Timeframe.W1)),

        // the dotted spellings
        ["prev_day.open > 0"] = On15(Of("prev_day_open", M15)),
        ["prev_day.high > 0"] = On15(Of("prev_day_high", M15)),
        ["prev_day.low > 0"] = On15(Of("prev_day_low", M15)),
        ["prev_day.close > 0"] = On15(Of("prev_day_close", M15)),
        ["prev_day.volume > 0"] = On15(Of("prev_day_volume", M15)),
        ["today.open > 0"] = On15(Of("today_open", M15)),
        ["today.high > 0"] = On15(Of("today_high", M15)),
        ["today.low > 0"] = On15(Of("today_low", M15)),
        ["index.trend == 1"] = On15(Of("index_trend", Timeframe.M1)),
        ["index.adx > 20"] = On15(Of("index_adx", Timeframe.M1)),

        // the calendar and the context are read on 1m whatever the strategy timeframe is
        ["day_of_week == TUE"] = On15(Of("day_of_week", Timeframe.M1)),
        ["is_expiry_day == 1"] = At(Timeframe.D1, Of("is_expiry_day", Timeframe.M1)),
        ["vix > 20"] = On15(Of("vix", Timeframe.M1)),
        ["regime() == 1"] = On15(Of("regime", Timeframe.M1)),
        ["regime() == \"DOWN_HIGH_VOL\""] = At(Timeframe.W1, Of("regime", Timeframe.M1)),
        ["tf(\"1m\").vix > 20"] = On15(Of("vix", Timeframe.M1)),

        // strategy parameters become part of the spec
        ["rsi(rsi_len) > 50"] = With(new Dictionary<string, double> { ["rsi_len"] = 21 }, Rsi(M15, 21)),
        ["rsi(rsi_len * 2) > 50"] = With(new Dictionary<string, double> { ["rsi_len"] = 7 }, Rsi(M15)),
        ["bb_upper(n, k) > 0"] = With(new Dictionary<string, double> { ["n"] = 10, ["k"] = 2.5 }, Of("bb_upper", M15, 10, 2.5)),
        ["ema(fast) > ema(slow)"] = With(new Dictionary<string, double> { ["fast"] = 9, ["slow"] = 21 }, Ema(M15, 9), Ema(M15, 21)),
        ["ema(min(fast, 5) + 1) > 0"] = With(new Dictionary<string, double> { ["fast"] = 9 }, Ema(M15, 6)),
        ["ema(-(-20)) > 0"] = On15(Ema(M15)),
        ["ema(abs(0 - 3)) > 0"] = On15(Ema(M15, 3)),
        ["ema(MON) > 0"] = On15(Ema(M15, 1)),
    };

    public static TheoryData<string> Sources { get; } = [.. Cases.Keys];
}
