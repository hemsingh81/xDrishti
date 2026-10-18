using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Lint.LintCodes;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Lint;

/// <summary>A condition, the strategy timeframe and parameters it is compiled with, and the problems it must produce, in source order.</summary>
internal sealed record LintCase(string Source, Timeframe Evaluation, Expected[] Problems, IReadOnlyDictionary<string, double> Parameters);

/// <summary>A condition that is close to a failing one but must compile: it must not produce the problem the failing example does (and, being valid, no error at all).</summary>
internal sealed record NearMiss(string Source, Timeframe Evaluation, string Code, IReadOnlyDictionary<string, double> Parameters);

/// <summary>
/// The lint table of the spec P3-02, one failing example or more and one near-miss or more for every row. The spans are given as the text they cover and located by searching the source; the suggestions and the
/// severities are written out. The exact messages are in <see cref="LintMessages"/>. The first word of a label is the code of the row.
/// </summary>
internal static class LintCases
{
    private static readonly IReadOnlyDictionary<string, double> None = new Dictionary<string, double>();

    private const Timeframe M15 = Timeframe.M15;

    private static Expected E(string code, string at, string? suggestion = null, int nth = 0) => new(code, at, suggestion, null, ProblemSeverity.Error, nth);

    private static Expected W(string code, string at, string? suggestion = null, int nth = 0) => Expected.Warning(code, at, suggestion, null, nth);

    private static Expected I(string code, string at, int nth = 0) => Expected.Info(code, at, null, nth);

    private static LintCase On(Timeframe timeframe, string source, params Expected[] problems) => new(source, timeframe, problems, None);

    private static LintCase C(string source, params Expected[] problems) => On(M15, source, problems);

    private static LintCase P(string source, IReadOnlyDictionary<string, double> parameters, params Expected[] problems) => new(source, M15, problems, parameters);

    private static NearMiss Ok(string code, string source, Timeframe timeframe = M15, IReadOnlyDictionary<string, double>? parameters = null) => new(source, timeframe, code, parameters ?? None);

    public static readonly IReadOnlyDictionary<string, LintCase> Failing = new Dictionary<string, LintCase>
    {
        // unknown_name
        ["unknown_name: a typo of a feature"] = C("closee > 1", E(UnknownName, "closee", "close")),
        ["unknown_name: a typo of a function"] = C("rsii(14) > 55", E(UnknownName, "rsii", "rsi")),
        ["unknown_name: the wrong case"] = C("RSI(14) > 55", E(UnknownName, "RSI", "rsi")),
        ["unknown_name: a constant in lower case"] = C("day_of_week == mon", E(UnknownName, "mon", "MON")),
        ["unknown_name: a name with no neighbour"] = C("banana > 1", E(UnknownName, "banana")),
        ["unknown_name: a typo of a parameter"] = P("rsi(rsi_lne) > 55", Params(("rsi_len", 14)), E(UnknownName, "rsi_lne", "rsi_len")),
        ["unknown_name: a parameter that is not given"] = C("rsi(rsi_len) > 55", E(UnknownName, "rsi_len")),
        ["unknown_name: a typo of a scope"] = C("todya.open > 1", E(UnknownName, "todya", "today")),
        ["unknown_name: a typo after a scope"] = C("prev_day.hgih > 1", E(UnknownName, "prev_day.hgih", "prev_day.high")),
        ["unknown_name: a dotted name that is spelled with an underscore"] = C("index.vol_pct > 50", E(UnknownName, "index.vol_pct", "index_vol_pct")),
        ["unknown_name: a regime name in lower case"] = C("regime() == \"up_low_vol\"", E(UnknownName, "\"up_low_vol\"", "\"UP_LOW_VOL\"")),
        ["unknown_name: a function with a timeframe"] = C("tf(\"1h\").crosses_above(close, vwap)", E(UnknownName, "crosses_above")),
        ["unknown_name: a parameter with a timeframe"] = P("tf(\"1h\").rsi_len > 1", Params(("rsi_len", 14)), E(UnknownName, "rsi_len")),
        ["unknown_name: unknown names inside an unknown call"] = C("banana(closee) > 1", E(UnknownName, "banana"), E(UnknownName, "closee", "close")),

        // bad_arguments
        ["bad_arguments: below the range"] = C("rsi(0) > 55", E(BadArguments, "0")),
        ["bad_arguments: above the range"] = C("rsi(10001) > 55", E(BadArguments, "10001")),
        ["bad_arguments: not a whole number"] = C("rsi(14.5) > 55", E(BadArguments, "14.5")),
        ["bad_arguments: one argument too many"] = C("rsi(14, 3) > 55", E(BadArguments, "3")),
        ["bad_arguments: several arguments too many"] = C("or_low(15, 3, 4) > 1", E(BadArguments, "3, 4")),
        ["bad_arguments: a feature with parameters written without parentheses"] = C("rsi > 55", E(BadArguments, "rsi", "rsi()")),
        ["bad_arguments: fast not below slow"] = C("macd_line(26, 12) > 0", E(BadArguments, "26")),
        ["bad_arguments: a band width of zero"] = C("bb_upper(25, 0) > 1", E(BadArguments, "0")),
        ["bad_arguments: an opening range that is not a whole number of bars"] = C("or_high(7) > 1", E(BadArguments, "7")),
        ["bad_arguments: a default that the timeframe refuses"] = On(Timeframe.M30, "or_high() > 1", E(BadArguments, "or_high()")),
        ["bad_arguments: an argument that is not a number"] = C("rsi(close > 1) > 55", E(BadArguments, "close > 1")),
        ["bad_arguments: an argument that changes with the data"] = C("rsi(close) > 55", E(BadArguments, "close")),
        ["bad_arguments: a source that is not a price"] = C("ema(20, source=volume) > 1", E(BadArguments, "volume")),
        ["bad_arguments: a typo of a source"] = C("ema(20, source=clsoe) > 1", E(BadArguments, "clsoe", "close")),
        ["bad_arguments: a source for a feature that reads the whole bar"] = C("atr(14, source=high) > 1", E(BadArguments, "source=high")),
        ["bad_arguments: the source given twice"] = C("ema(20, source=high, source=low) > 1", E(BadArguments, "source=low")),
        ["bad_arguments: a named argument that is not the source"] = C("ema(n=high) > 1", E(BadArguments, "n=high")),
        ["bad_arguments: a function written without parentheses"] = C("abs > 1", E(BadArguments, "abs")),
        ["bad_arguments: too few arguments for a function"] = C("crosses_above(close)", E(BadArguments, "crosses_above(close)")),
        ["bad_arguments: too many arguments for a function"] = C("between(rsi(14), 30, 70, 90)", E(BadArguments, "90")),
        ["bad_arguments: a named argument for a function"] = C("abs(x=close) > 1", E(BadArguments, "x=close")),
        ["bad_arguments: a parameter called like a function"] = P("rsi_len(14) > 1", Params(("rsi_len", 14)), E(BadArguments, "rsi_len")),
        ["bad_arguments: the time called like a function"] = C("time() > 1", E(BadArguments, "time")),
        ["bad_arguments: a count that changes with the data"] = C("rising(close, close)", E(BadArguments, "close", nth: 1)),
        ["bad_arguments: a count that is not finite"] = C("rising(close, 1 / 0)", E(BadArguments, "1 / 0")),
        ["bad_arguments: an argument that is not finite"] = C("rsi(1 / 0) > 5", E(BadArguments, "1 / 0")),
        ["bad_arguments: tf without a feature"] = C("tf > 1", E(BadArguments, "tf")),
        ["bad_arguments: a qualified feature with parameters written without parentheses"] = C("tf(\"1h\").ema > 1", E(BadArguments, "tf(\"1h\").ema", "ema()")),

        // unsupported_timeframe
        ["unsupported_timeframe: 1d in lower case"] = C("tf(\"1d\").close > 0", E(UnsupportedTimeframe, "\"1d\"", "\"1D\"")),
        ["unsupported_timeframe: 1H in upper case"] = C("tf(\"1H\").close > 0", E(UnsupportedTimeframe, "\"1H\"", "\"1h\"")),
        ["unsupported_timeframe: a timeframe near a real one"] = C("tf(\"2h\").close > 0", E(UnsupportedTimeframe, "\"2h\"", "\"1h\"")),
        ["unsupported_timeframe: an empty timeframe"] = C("tf(\"\").close > 0", E(UnsupportedTimeframe, "\"\"")),
        ["unsupported_timeframe: vwap on 1D by tf"] = C("tf(\"1D\").vwap > 0", E(UnsupportedTimeframe, "\"1D\"")),
        ["unsupported_timeframe: vwap on a daily strategy"] = On(Timeframe.D1, "vwap > 0", E(UnsupportedTimeframe, "vwap")),
        ["unsupported_timeframe: the previous day on a weekly strategy"] = On(Timeframe.W1, "prev_day.high > 0", E(UnsupportedTimeframe, "prev_day.high")),
        ["unsupported_timeframe: today's value on 1W by tf"] = C("tf(\"1W\").today_open > 0", E(UnsupportedTimeframe, "\"1W\"")),
        ["unsupported_timeframe: a date feature on 1h"] = C("tf(\"1h\").vix > 0", E(UnsupportedTimeframe, "\"1h\"")),

        // negative_or_zero_lag
        // a subscript of zero is refused as a whole: the span is `close[0]` and the suggestion `close` replaces it (a negative lag keeps the number as its span and has no suggestion)
        ["negative_or_zero_lag: a subscript of zero"] = C("close[0] > 1", E(NegativeOrZeroLag, "close[0]", "close")),
        ["negative_or_zero_lag: a subscript of minus zero"] = C("close[-0] > 1", E(NegativeOrZeroLag, "close[-0]", "close")),
        ["negative_or_zero_lag: a subscript of two zeros"] = C("close[00] > 1", E(NegativeOrZeroLag, "close[00]", "close")),
        ["negative_or_zero_lag: a subscript of zero on a dotted feature"] = C("prev_day.high[0] > 1", E(NegativeOrZeroLag, "prev_day.high[0]", "prev_day.high")),
        ["negative_or_zero_lag: a subscript of zero on a qualified feature"] = C("tf(\"1h\").close[0] > 1", E(NegativeOrZeroLag, "tf(\"1h\").close[0]", "tf(\"1h\").close")),

        // the problem of the target comes first (it is found first and starts where the subscript starts), and a target that could not be bound has no suggestion to offer: `closee` would overlap its own problem
        ["negative_or_zero_lag: a subscript of zero on an unknown name has no suggestion"] = C("closee[0] > 1", E(UnknownName, "closee", "close"), E(NegativeOrZeroLag, "closee[0]")),
        ["negative_or_zero_lag: a subscript of zero on a feature of a timeframe that does not exist"] = C(
            "tf(\"2h\").close[0] > 1", E(NegativeOrZeroLag, "tf(\"2h\").close[0]"), E(UnsupportedTimeframe, "\"2h\"", "\"1h\"")),
        ["negative_or_zero_lag: a subscript of zero on a name that is not a feature"] = P(
            "rsi_len[0] > 1", Params(("rsi_len", 14)), E(LagOnNonFeature, "rsi_len"), E(NegativeOrZeroLag, "rsi_len[0]", "rsi_len")),
        ["negative_or_zero_lag: a negative subscript"] = C("close[-1] > 1", E(NegativeOrZeroLag, "-1")),
        ["negative_or_zero_lag: a rising count of zero"] = C("rising(close, 0)", E(NegativeOrZeroLag, "0")),
        ["negative_or_zero_lag: a rising count that is not whole"] = C("rising(close, 1.5)", E(NegativeOrZeroLag, "1.5")),
        ["negative_or_zero_lag: a negative falling count"] = C("falling(close, -2)", E(NegativeOrZeroLag, "-2")),
        ["negative_or_zero_lag: a count from a parameter"] = P("rising(close, rise_len)", Params(("rise_len", 0)), E(NegativeOrZeroLag, "rise_len")),

        // lag_too_deep
        ["lag_too_deep: a subscript of 256"] = C("close[256] > 1", E(LagTooDeep, "close[256]")),
        ["lag_too_deep: a huge subscript"] = C("close[100000] > 1", E(LagTooDeep, "close[100000]")),
        ["lag_too_deep: a rising count of 256"] = C("rising(close, 256)", E(LagTooDeep, "rising(close, 256)")),
        ["lag_too_deep: lags that add up inside rising"] = C("rising(close[200], 100)", E(LagTooDeep, "rising(close[200], 100)")),
        // the vwap of the first crossing is read one bar back (15 minutes): no warning; the second reads it 255 + 1 bars of 15m back, far more than a session holds (25 bars)
        ["lag_too_deep: the extra lag of a crossing"] = C(
            "crosses_above(close[255], vwap)", E(LagTooDeep, "crosses_above(close[255], vwap)")),
        ["lag_too_deep: the extra lag of a downward crossing"] = C(
            "crosses_below(close, vwap[255])", E(LagTooDeep, "crosses_below(close, vwap[255])"), W(LagAcrossSession, "crosses_below(close, vwap[255])")),
        ["lag_too_deep: two places"] = C("close[300] > 1 and close[400] > 1", E(LagTooDeep, "close[300]"), E(LagTooDeep, "close[400]")),

        ["lag_too_deep: a type error in a side does not hide the depth of a crossing"] = C(
            "crosses_above(close[255] + true, vwap)", E(LagTooDeep, "crosses_above(close[255] + true, vwap)"), E(TypeMismatch, "true")),
        ["lag_too_deep: a type error in the operand does not hide the depth of rising"] = C(
            "rising(close[250] + true, 10)", E(LagTooDeep, "rising(close[250] + true, 10)"), E(TypeMismatch, "true")),

        // lag_on_non_feature
        ["lag_on_non_feature: a parameter"] = P("rsi_len[1] > 1", Params(("rsi_len", 14)), E(LagOnNonFeature, "rsi_len")),
        ["lag_on_non_feature: a constant"] = C("MON[1] > 1", E(LagOnNonFeature, "MON")),
        ["lag_on_non_feature: the time"] = C("time[1] > 1", E(LagOnNonFeature, "time")),

        // mixed_timeframes_in_lag
        ["mixed_timeframes_in_lag: a crossing of the close and a daily average"] = C(
            "crosses_above(close, tf(\"1D\").ema(20))", E(MixedTimeframesInLag, "crosses_above(close, tf(\"1D\").ema(20))", "close > tf(\"1D\").ema(20) and close[1] <= tf(\"1D\").ema(20)")),
        ["mixed_timeframes_in_lag: a downward crossing where only the second side can be lagged by hand"] = C(
            "crosses_below(tf(\"1h\").ema(20), close)", E(MixedTimeframesInLag, "crosses_below(tf(\"1h\").ema(20), close)", "tf(\"1h\").ema(20) < close and tf(\"1h\").ema(20) >= close[1]")),
        ["mixed_timeframes_in_lag: a crossing of two calls has no explicit form"] = C("crosses_below(tf(\"1h\").ema(20), ema(50))", E(MixedTimeframesInLag, "crosses_below(tf(\"1h\").ema(20), ema(50))")),
        ["mixed_timeframes_in_lag: a sum of two timeframes in rising"] = C("rising(close + tf(\"1h\").close, 2)", E(MixedTimeframesInLag, "rising(close + tf(\"1h\").close, 2)")),
        ["mixed_timeframes_in_lag: a difference of two timeframes in falling"] = C("falling(close - tf(\"1D\").close, 1)", E(MixedTimeframesInLag, "falling(close - tf(\"1D\").close, 1)")),
        ["mixed_timeframes_in_lag: a feature of the context against a bar feature"] = C("crosses_above(close, vix)", E(MixedTimeframesInLag, "crosses_above(close, vix)", "close > vix and close[1] <= vix")),
        ["mixed_timeframes_in_lag: the context against a bar feature on a 1m strategy"] = On(
            Timeframe.M1, "crosses_above(close, vix)", E(MixedTimeframesInLag, "crosses_above(close, vix)", "close > vix and close[1] <= vix")),
        ["mixed_timeframes_in_lag: the context first on a 1m strategy lags the bar feature by hand"] = On(
            Timeframe.M1, "crosses_below(vix, close)", E(MixedTimeframesInLag, "crosses_below(vix, close)", "vix < close and vix >= close[1]")),
        ["mixed_timeframes_in_lag: the index trend against a bar feature on a 1m strategy"] = On(
            Timeframe.M1, "crosses_above(close, index.trend)", E(MixedTimeframesInLag, "crosses_above(close, index.trend)", "close > index.trend and close[1] <= index.trend")),
        ["mixed_timeframes_in_lag: a product of a bar feature and the context in rising on a 1m strategy"] = On(
            Timeframe.M1, "rising(close * vix, 2)", E(MixedTimeframesInLag, "rising(close * vix, 2)")),
        ["mixed_timeframes_in_lag: a calendar feature against a bar feature in falling on a 1m strategy"] = On(
            Timeframe.M1, "falling(close - day_of_week, 1)", E(MixedTimeframesInLag, "falling(close - day_of_week, 1)")),
        ["mixed_timeframes_in_lag: a 1m bar feature written with tf against the context on a 15m strategy"] = C(
            "crosses_above(tf(\"1m\").close, vix)", E(MixedTimeframesInLag, "crosses_above(tf(\"1m\").close, vix)")),
        ["mixed_timeframes_in_lag: a 1m bar feature written with tf against the context on a daily strategy"] = On(
            Timeframe.D1, "crosses_above(tf(\"1m\").close, vix)", E(MixedTimeframesInLag, "crosses_above(tf(\"1m\").close, vix)")),
        ["mixed_timeframes_in_lag: the context against a bar feature on a daily strategy"] = On(
            Timeframe.D1, "crosses_below(close, breadth)", E(MixedTimeframesInLag, "crosses_below(close, breadth)", "close < breadth and close[1] >= breadth")),
        ["mixed_timeframes_in_lag: two bar timeframes and the context"] = C(
            "rising(close + tf(\"1h\").close - vix, 2)", E(MixedTimeframesInLag, "rising(close + tf(\"1h\").close - vix, 2)")),
        ["mixed_timeframes_in_lag: only the second crossing mixes"] = C(
            "crosses_above(close, vwap) and crosses_above(close, tf(\"1W\").close)",
            E(MixedTimeframesInLag, "crosses_above(close, tf(\"1W\").close)", "close > tf(\"1W\").close and close[1] <= tf(\"1W\").close")),

        // a fault in one part of a call does not hide the clock rules for the rest of it: both are reported together, whichever is fixed first
        ["mixed_timeframes_in_lag: a first side that already has a subscript cannot be lagged by hand"] = C(
            "crosses_above(close[1], tf(\"1D\").close)", E(MixedTimeframesInLag, "crosses_above(close[1], tf(\"1D\").close)")),
        ["mixed_timeframes_in_lag: a second side that already has a subscript cannot be lagged by hand"] = C(
            "crosses_below(tf(\"1h\").close, close[2])", E(MixedTimeframesInLag, "crosses_below(tf(\"1h\").close, close[2])")),
        ["mixed_timeframes_in_lag: a count that is not whole does not hide the mix in rising"] = C(
            "rising(close + tf(\"1h\").close, 0.5)", E(MixedTimeframesInLag, "rising(close + tf(\"1h\").close, 0.5)"), E(NegativeOrZeroLag, "0.5")),
        ["mixed_timeframes_in_lag: a count that is not finite does not hide the mix in falling"] = C(
            "falling(close - tf(\"1D\").close, 1 / 0)", E(MixedTimeframesInLag, "falling(close - tf(\"1D\").close, 1 / 0)"), E(BadArguments, "1 / 0")),
        ["mixed_timeframes_in_lag: a type error in one side does not hide the mix in a crossing"] = C(
            "crosses_above(close + true, tf(\"1h\").ema(20))", E(MixedTimeframesInLag, "crosses_above(close + true, tf(\"1h\").ema(20))"), E(TypeMismatch, "true")),
        ["mixed_timeframes_in_lag: a type error in the operand does not hide the mix in rising"] = C(
            "rising(close + true + tf(\"1h\").close, 2)", E(MixedTimeframesInLag, "rising(close + true + tf(\"1h\").close, 2)"), E(TypeMismatch, "true")),

        // lag_across_session (warning): a read from further back than a session holds (k bars of T minutes with k · T at least 375), whatever the strategy's timeframe; 25 bars of 15m, 75 of 5m, 7 of 1h
        ["lag_across_session: the first subscript that a 5m session cannot hold"] = On(Timeframe.M5, "close > vwap[75]", W(LagAcrossSession, "vwap[75]")),
        ["lag_across_session: the first count that an hourly session cannot hold"] = On(Timeframe.H1, "rising(vwap, 7)", W(LagAcrossSession, "rising(vwap, 7)")),
        ["lag_across_session: the first count that a 5m session cannot hold"] = On(Timeframe.M5, "rising(vwap, 75)", W(LagAcrossSession, "rising(vwap, 75)")),
        ["lag_across_session: the first count that a 15m session cannot hold"] = C("falling(today_low, 25)", W(LagAcrossSession, "falling(today_low, 25)")),
        ["lag_across_session: a subscript on 1m at the whole session"] = On(Timeframe.M1, "close > vwap[375]", E(LagTooDeep, "vwap[375]"), W(LagAcrossSession, "vwap[375]")),
        ["lag_across_session: a 5m feature read on a daily strategy"] = On(Timeframe.D1, "tf(\"5m\").vwap[80] > 0", W(LagAcrossSession, "tf(\"5m\").vwap[80]")),
        ["lag_across_session: a time check does not clear a read that is never known"] = C(
            "rising(vwap, 25) and time >= \"10:00\"", W(LagAcrossSession, "rising(vwap, 25)")),
        ["lag_across_session: the extra lag of a crossing counts"] = On(Timeframe.M5, "crosses_above(close, vwap[74])", W(LagAcrossSession, "crosses_above(close, vwap[74])")),
        ["lag_across_session: the lags of a subscript and of rising add up"] = C("rising(vwap[20], 5)", W(LagAcrossSession, "rising(vwap[20], 5)")),
        ["lag_across_session: two features in one call give one warning"] = C("rising(vwap - today_open, 25)", W(LagAcrossSession, "rising(vwap - today_open, 25)")),
        ["lag_across_session: two calls give two warnings"] = C(
            "rising(vwap, 25) and rising(today_low, 30)", W(LagAcrossSession, "rising(vwap, 25)"), W(LagAcrossSession, "rising(today_low, 30)")),
        ["lag_across_session: a feature of another timeframe"] = C("rising(tf(\"1h\").vwap, 7)", W(LagAcrossSession, "rising(tf(\"1h\").vwap, 7)")),
        ["lag_across_session: an opening range"] = C("rising(or_high(15), 25)", W(LagAcrossSession, "rising(or_high(15), 25)")),
        ["lag_across_session: the previous day's value"] = C("rising(prev_day.high, 30)", W(LagAcrossSession, "rising(prev_day.high, 30)")),
        ["lag_across_session: a type error in an operand does not hide it"] = C("rising(vwap + true, 25)", W(LagAcrossSession, "rising(vwap + true, 25)"), E(TypeMismatch, "true")),

        // time_on_daily
        ["time_on_daily: a daily strategy"] = On(Timeframe.D1, "time >= \"09:45\"", E(TimeOnDaily, "time")),
        ["time_on_daily: a weekly strategy"] = On(Timeframe.W1, "time >= \"09:45\"", E(TimeOnDaily, "time")),
        ["time_on_daily: a monthly strategy"] = On(Timeframe.Mo1, "time >= \"09:45\"", E(TimeOnDaily, "time")),
        ["time_on_daily: the time further in the condition"] = On(Timeframe.D1, "close > 1 and \"10:00\" > time", E(TimeOnDaily, "time")),
        ["time_on_daily: a text that is not a time does not hide it"] = On(Timeframe.D1, "time >= \"9:45\"", E(TimeOnDaily, "time"), E(BadTimeLiteral, "\"9:45\"", "\"09:45\"")),
        ["time_on_daily: a number to compare the time with does not hide it"] = On(Timeframe.D1, "time > 5", E(TypeMismatch, "time > 5"), E(TimeOnDaily, "time")),
        ["time_on_daily: a number before the time does not hide it"] = On(Timeframe.D1, "5 < time", E(TypeMismatch, "5 < time"), E(TimeOnDaily, "time")),
        ["time_on_daily: a subscript on the time does not hide it"] = On(Timeframe.D1, "time[1] > 1", E(LagOnNonFeature, "time"), E(TimeOnDaily, "time")),

        // bad_time_literal
        ["bad_time_literal: one digit for the hour"] = C("time >= \"9:45\"", E(BadTimeLiteral, "\"9:45\"", "\"09:45\"")),
        ["bad_time_literal: before the open"] = C("time >= \"09:00\"", E(BadTimeLiteral, "\"09:00\"")),
        ["bad_time_literal: after the close"] = C("time >= \"15:31\"", E(BadTimeLiteral, "\"15:31\"")),
        ["bad_time_literal: an hour that does not exist"] = C("time >= \"25:00\"", E(BadTimeLiteral, "\"25:00\"")),
        ["bad_time_literal: with seconds"] = C("time >= \"09:45:00\"", E(BadTimeLiteral, "\"09:45:00\"")),
        ["bad_time_literal: a dot for the colon"] = C("time >= \"09.45\"", E(BadTimeLiteral, "\"09.45\"", "\"09:45\"")),
        ["bad_time_literal: an empty text"] = C("time >= \"\"", E(BadTimeLiteral, "\"\"")),
        ["bad_time_literal: the text before the time"] = C("\"abc\" < time", E(BadTimeLiteral, "\"abc\"")),

        // type_mismatch
        ["type_mismatch: a condition that is a number"] = C("rsi(14)", E(TypeMismatch, "rsi(14)")),
        ["type_mismatch: and of a number"] = C("rsi(14) and close > 1", E(TypeMismatch, "rsi(14)")),
        ["type_mismatch: or of a number on the right"] = C("close > 1 or rsi(14)", E(TypeMismatch, "rsi(14)")),
        ["type_mismatch: and of the time"] = C("time and close > 1", E(TypeMismatch, "time")),
        ["type_mismatch: not of a regime"] = C("not regime()", E(TypeMismatch, "regime()")),
        ["type_mismatch: the time against a number"] = C("time > 5", E(TypeMismatch, "time > 5")),
        ["type_mismatch: a number against a true/false value"] = C("close > true", E(TypeMismatch, "close > true")),
        ["type_mismatch: ordering a regime"] = C("regime() < 3", E(TypeMismatch, "regime() < 3")),
        ["type_mismatch: a regime code that does not exist"] = C("regime() == 10", E(TypeMismatch, "regime() == 10")),
        ["type_mismatch: not of a number"] = C("not close", E(TypeMismatch, "close")),
        ["type_mismatch: minus of a true/false value"] = C("-true > 1", E(TypeMismatch, "true")),
        ["type_mismatch: two texts"] = C("\"UP\" == \"UP\"", E(TypeMismatch, "\"UP\" == \"UP\"")),
        ["type_mismatch: two regimes"] = C("regime() == regime()", E(TypeMismatch, "regime() == regime()")),
        ["type_mismatch: ordering true/false values"] = C("true < false", E(TypeMismatch, "true < false")),
        ["type_mismatch: a condition that is a text"] = C("\"09:45\"", E(TypeMismatch, "\"09:45\"")),
        ["type_mismatch: a condition that is the time"] = C("time", E(TypeMismatch, "time")),
        ["type_mismatch: a sum with a true/false value"] = C("close + true > 1", E(TypeMismatch, "true")),
        ["type_mismatch: a regime against a feature"] = C("regime() == close", E(TypeMismatch, "regime() == close")),
        ["type_mismatch: a parameter that is not a number"] = P("close > bad", Params(("bad", double.NaN)), E(TypeMismatch, "bad")),
        ["type_mismatch: a regime in arithmetic"] = C("abs(regime()) > 1", E(TypeMismatch, "regime()")),

        // constant_condition (warning)
        ["constant_condition: one less than two"] = C("1 < 2", W(ConstantCondition, "1 < 2")),
        ["constant_condition: true"] = C("true", W(ConstantCondition, "true")),
        ["constant_condition: a crossing of constants"] = C("crosses_above(1, 2)", W(ConstantCondition, "crosses_above(1, 2)")),
        ["constant_condition: not false"] = C("not false", W(ConstantCondition, "not false")),
        ["constant_condition: one constant part among data"] = C("close > 1 and 2 > 1", W(ConstantCondition, "2 > 1")),
        ["constant_condition: a parameter against a number"] = P("level > 50", Params(("level", 55)), W(ConstantCondition, "level > 50")),
        ["constant_condition: between constants"] = C("between(5, 1, 10)", W(ConstantCondition, "between(5, 1, 10)")),

        // redundant_timeframe (warning)
        ["redundant_timeframe: the strategy's own timeframe"] = C("tf(\"15m\").ema(20) > 0", W(RedundantTimeframe, "tf(\"15m\").ema(20)", "ema(20)")),
        ["redundant_timeframe: an hourly strategy"] = On(Timeframe.H1, "tf(\"1h\").close > 0", W(RedundantTimeframe, "tf(\"1h\").close", "close")),
        ["redundant_timeframe: a feature of the day on 1m"] = On(Timeframe.M1, "tf(\"1m\").vix > 0", W(RedundantTimeframe, "tf(\"1m\").vix", "vix"), I(NeedsContext, "tf(\"1m\").vix")),

        // needs_context and needs_expiry_days (information)
        ["needs_context: the regime"] = C("regime() == 1", I(NeedsContext, "regime()")),
        ["needs_context: two context features give one note"] = C("vix > 20 and breadth > 50", I(NeedsContext, "vix")),
        ["needs_expiry_days: the expiry day"] = C("is_expiry_day == 1", I(NeedsExpiryDays, "is_expiry_day")),
        ["needs_expiry_days: both notes in source order"] = C("index.trend == 1 and is_expiry_day == 0", I(NeedsContext, "index.trend"), I(NeedsExpiryDays, "is_expiry_day")),

        // shadowed_name (warning)
        ["shadowed_name: a parameter named like a feature"] = P("rsi(14) > close", Params(("close", 5)), W(ShadowedName, "close")),
        ["shadowed_name: a parameter named like a constant"] = P("day_of_week == MON", Params(("MON", 3)), W(ShadowedName, "MON")),
        ["shadowed_name: a parameter named like a function"] = P("rsi(14) > max", Params(("max", 3)), W(ShadowedName, "max")),
    };

    public static readonly IReadOnlyDictionary<string, NearMiss> Passing = new Dictionary<string, NearMiss>
    {
        ["unknown_name: a known feature"] = Ok(UnknownName, "close > 1"),
        ["unknown_name: a known parameter"] = Ok(UnknownName, "rsi(rsi_len) > 55", parameters: Params(("rsi_len", 14))),
        ["unknown_name: a dotted name"] = Ok(UnknownName, "prev_day.high > 1"),
        ["unknown_name: a regime name"] = Ok(UnknownName, "regime() == \"UP_LOW_VOL\""),
        ["unknown_name: a constant"] = Ok(UnknownName, "day_of_week == MON"),
        ["bad_arguments: the smallest period"] = Ok(BadArguments, "rsi(1) > 55"),
        ["bad_arguments: the largest period"] = Ok(BadArguments, "rsi(10000) > 55"),
        ["bad_arguments: fast below slow"] = Ok(BadArguments, "macd_line(12, 26) > 0"),
        ["bad_arguments: the widest band"] = Ok(BadArguments, "bb_upper(20, 100) > 0"),
        ["bad_arguments: a narrow band"] = Ok(BadArguments, "bb_upper(20, 0.001) > 0"),
        ["bad_arguments: an opening range of whole bars"] = Ok(BadArguments, "or_high(30) > 0"),
        ["bad_arguments: a price source"] = Ok(BadArguments, "ema(20, source=low) > 1"),
        ["bad_arguments: arithmetic in an argument"] = Ok(BadArguments, "rsi(7 * 2) > 55"),
        ["bad_arguments: a function with its arguments"] = Ok(BadArguments, "between(rsi(14), 30, 70)"),
        ["unsupported_timeframe: vwap on 1m"] = Ok(UnsupportedTimeframe, "tf(\"1m\").vwap > 0"),
        ["unsupported_timeframe: the close on 1D"] = Ok(UnsupportedTimeframe, "tf(\"1D\").close > 0"),
        ["unsupported_timeframe: a date feature on 1m"] = Ok(UnsupportedTimeframe, "tf(\"1m\").vix > 0"),
        ["unsupported_timeframe: the previous day on 1D"] = Ok(UnsupportedTimeframe, "prev_day.high > 0", Timeframe.D1),
        ["unsupported_timeframe: the weekly close"] = Ok(UnsupportedTimeframe, "tf(\"1W\").close > 0"),
        ["negative_or_zero_lag: a subscript of one"] = Ok(NegativeOrZeroLag, "close[1] > 1"),
        ["negative_or_zero_lag: a rising count of one"] = Ok(NegativeOrZeroLag, "rising(close, 1)"),
        ["negative_or_zero_lag: a falling count of 255"] = Ok(NegativeOrZeroLag, "falling(close, 255)"),
        ["lag_too_deep: a subscript of 255"] = Ok(LagTooDeep, "close[255] > 1"),
        ["lag_too_deep: a rising count of 255"] = Ok(LagTooDeep, "rising(close, 255)"),
        ["lag_too_deep: a crossing of a lag of 254"] = Ok(LagTooDeep, "crosses_above(close[254], vwap)"),
        ["lag_too_deep: lags that add up to 255"] = Ok(LagTooDeep, "rising(close[100], 155)"),
        ["lag_too_deep: lags that add up to 255 the other way"] = Ok(LagTooDeep, "rising(close[155], 100)"),
        ["lag_on_non_feature: a feature"] = Ok(LagOnNonFeature, "close[1] > 1"),
        ["lag_on_non_feature: a dotted feature"] = Ok(LagOnNonFeature, "prev_day.high[1] > 1"),
        ["lag_on_non_feature: a qualified feature"] = Ok(LagOnNonFeature, "tf(\"1h\").close[1] > 1"),
        ["lag_on_non_feature: a feature of the day"] = Ok(LagOnNonFeature, "vix[1] > 1"),
        ["mixed_timeframes_in_lag: one timeframe"] = Ok(MixedTimeframesInLag, "crosses_above(close, ema(20))"),
        ["mixed_timeframes_in_lag: two daily features"] = Ok(MixedTimeframesInLag, "crosses_above(tf(\"1D\").close, tf(\"1D\").ema(20))"),
        ["mixed_timeframes_in_lag: the explicit form"] = Ok(MixedTimeframesInLag, "close > tf(\"1D\").ema(20) and close[1] <= tf(\"1D\").ema(20)"),
        ["mixed_timeframes_in_lag: two risings on two timeframes"] = Ok(MixedTimeframesInLag, "rising(close, 3) and rising(tf(\"1h\").close, 3)"),
        ["mixed_timeframes_in_lag: a constant and a feature"] = Ok(MixedTimeframesInLag, "crosses_above(close, 100)"),
        ["mixed_timeframes_in_lag: the strategy timeframe written out"] = Ok(MixedTimeframesInLag, "crosses_above(close, tf(\"15m\").close)"),
        ["mixed_timeframes_in_lag: the context against a number"] = Ok(MixedTimeframesInLag, "crosses_above(vix, 20)"),
        ["mixed_timeframes_in_lag: the context against a number on a 1m strategy"] = Ok(MixedTimeframesInLag, "crosses_above(vix, 20)", Timeframe.M1),
        ["mixed_timeframes_in_lag: a rising context"] = Ok(MixedTimeframesInLag, "rising(vix, 2)"),
        ["mixed_timeframes_in_lag: a falling context on a 1m strategy"] = Ok(MixedTimeframesInLag, "falling(vix, 2)", Timeframe.M1),
        ["mixed_timeframes_in_lag: two features of the context"] = Ok(MixedTimeframesInLag, "crosses_above(vix, breadth)"),
        ["mixed_timeframes_in_lag: two features of the context on a 1m strategy"] = Ok(MixedTimeframesInLag, "crosses_below(vix, index_trend)", Timeframe.M1),
        ["mixed_timeframes_in_lag: the context and the calendar in rising"] = Ok(MixedTimeframesInLag, "rising(vix - day_of_week, 2)", Timeframe.M1),
        ["mixed_timeframes_in_lag: a calendar feature against a constant"] = Ok(MixedTimeframesInLag, "crosses_above(day_of_week, 3)", Timeframe.M1),
        ["mixed_timeframes_in_lag: the regime against a code"] = Ok(MixedTimeframesInLag, "regime() == 3", Timeframe.M1),
        ["mixed_timeframes_in_lag: the explicit form against the context on a 1m strategy"] = Ok(MixedTimeframesInLag, "close > vix and close[1] <= vix", Timeframe.M1),
        ["mixed_timeframes_in_lag: the explicit form against the context on a 15m strategy"] = Ok(MixedTimeframesInLag, "close > vix and close[1] <= vix"),
        ["mixed_timeframes_in_lag: a run of the bars and a run of the context in two calls"] = Ok(MixedTimeframesInLag, "rising(close, 3) and rising(vix, 3)", Timeframe.M1),
        ["mixed_timeframes_in_lag: a bar feature of the strategy and the same feature written with tf on 1m"] = Ok(MixedTimeframesInLag, "crosses_above(close, tf(\"1m\").close)", Timeframe.M1),
        ["lag_across_session: no lag"] = Ok(LagAcrossSession, "close > vwap"),
        ["lag_across_session: a continuous feature runs on across sessions"] = Ok(LagAcrossSession, "rising(ema(20), 200)"),
        ["lag_across_session: the context is read once a date"] = Ok(LagAcrossSession, "rising(vix, 2)"),
        ["lag_across_session: the headline crossing needs no check on 1m"] = Ok(LagAcrossSession, "crosses_above(close, vwap)", Timeframe.M1),
        ["lag_across_session: the headline crossing needs no check on 5m"] = Ok(LagAcrossSession, "crosses_above(close, vwap)", Timeframe.M5),
        ["lag_across_session: the headline crossing needs no check on 15m"] = Ok(LagAcrossSession, "crosses_above(close, vwap)"),
        ["lag_across_session: the headline crossing needs no check on 1h"] = Ok(LagAcrossSession, "crosses_above(close, vwap)", Timeframe.H1),
        ["lag_across_session: the headline crossing with a check of the time is not warned either"] = Ok(LagAcrossSession, "crosses_above(close, vwap) and time >= \"09:45\""),
        ["lag_across_session: the crossing written out lags the vwap by hand"] = Ok(LagAcrossSession, "close > vwap and close[1] <= vwap[1]"),
        ["lag_across_session: a daily strategy reads the daily value by position"] = Ok(LagAcrossSession, "crosses_above(close, prev_day.high)", Timeframe.D1),
        ["lag_across_session: a daily feature read on a daily strategy"] = Ok(LagAcrossSession, "rising(today_open, 200)", Timeframe.D1),
        ["lag_across_session: a daily gap read far back"] = Ok(LagAcrossSession, "tf(\"1D\").gap_pct[200] > 0"),
        ["lag_across_session: an opening range of several bars crossing"] = Ok(LagAcrossSession, "crosses_above(close, or_high(30))"),
        ["lag_across_session: an opening range on 1m crossing"] = Ok(LagAcrossSession, "crosses_above(close, or_low())", Timeframe.M1),
        ["lag_across_session: a 1m feature read as far back as the frame keeps"] = Ok(LagAcrossSession, "rising(tf(\"1m\").vwap, 255)"),
        ["lag_across_session: one bar short of a 5m session"] = Ok(LagAcrossSession, "close > vwap[74]", Timeframe.M5),
        ["lag_across_session: one bar short of a 10m session"] = Ok(LagAcrossSession, "close > vwap[37]", Timeframe.M10),
        ["lag_across_session: one bar short of a 15m session"] = Ok(LagAcrossSession, "close > vwap[24]"),
        ["lag_across_session: one bar short of a 30m session"] = Ok(LagAcrossSession, "close > vwap[12]", Timeframe.M30),
        ["lag_across_session: one bar short of an hourly session"] = Ok(LagAcrossSession, "rising(vwap, 6)", Timeframe.H1),
        ["lag_across_session: the extra lag of a crossing stays short of a 5m session"] = Ok(LagAcrossSession, "crosses_above(close, vwap[73])", Timeframe.M5),
        ["lag_across_session: a long read that a session holds"] = Ok(LagAcrossSession, "rising(vwap, 24) and time >= \"10:00\""),
        ["lag_across_session: a 5m feature read short of a session on a daily strategy"] = Ok(LagAcrossSession, "tf(\"5m\").vwap[74] > 0", Timeframe.D1),
        ["time_on_daily: 15m"] = Ok(TimeOnDaily, "time >= \"09:45\""),
        ["time_on_daily: 1h"] = Ok(TimeOnDaily, "time >= \"09:45\"", Timeframe.H1),
        ["time_on_daily: 1m"] = Ok(TimeOnDaily, "time >= \"09:45\"", Timeframe.M1),
        ["time_on_daily: 5m"] = Ok(TimeOnDaily, "time >= \"09:45\"", Timeframe.M5),
        ["time_on_daily: a daily strategy without the time"] = Ok(TimeOnDaily, "close > 1", Timeframe.D1),
        ["bad_time_literal: the open"] = Ok(BadTimeLiteral, "time >= \"09:15\""),
        ["bad_time_literal: the close"] = Ok(BadTimeLiteral, "time <= \"15:30\""),
        ["bad_time_literal: a time inside"] = Ok(BadTimeLiteral, "time >= \"09:45\""),
        ["bad_time_literal: the text first"] = Ok(BadTimeLiteral, "\"09:45\" <= time"),
        ["type_mismatch: two true/false values"] = Ok(TypeMismatch, "true == false"),
        ["type_mismatch: a regime against a code"] = Ok(TypeMismatch, "regime() == 3"),
        ["type_mismatch: a regime against a name"] = Ok(TypeMismatch, "regime() != \"UP_LOW_VOL\""),
        ["type_mismatch: the time against a text"] = Ok(TypeMismatch, "time < \"10:00\""),
        ["type_mismatch: two comparisons compared"] = Ok(TypeMismatch, "(close > 1) == (open > 1)"),
        ["type_mismatch: a number given as a parameter"] = Ok(TypeMismatch, "close > bad", parameters: Params(("bad", 3))),
        ["constant_condition: a comparison of data"] = Ok(ConstantCondition, "rsi(14) > 55"),
        ["constant_condition: a bounded oscillator above 100 is not detected"] = Ok(ConstantCondition, "rsi(14) > 100"),
        ["constant_condition: true as one part among data"] = Ok(ConstantCondition, "close > 1 and true"),
        ["constant_condition: the time"] = Ok(ConstantCondition, "time >= \"09:45\""),
        ["redundant_timeframe: another timeframe"] = Ok(RedundantTimeframe, "tf(\"1h\").ema(20) > 0"),
        ["redundant_timeframe: the same timeframe on another strategy"] = Ok(RedundantTimeframe, "tf(\"15m\").close > 0", Timeframe.M5),
        ["redundant_timeframe: the default timeframe"] = Ok(RedundantTimeframe, "ema(20) > 0"),
        ["needs_context: a calendar feature"] = Ok(NeedsContext, "day_of_week == TUE"),
        ["needs_context: a bar feature"] = Ok(NeedsContext, "close > 1"),
        ["needs_expiry_days: a context feature"] = Ok(NeedsExpiryDays, "vix > 20"),
        ["shadowed_name: a parameter that hides nothing"] = Ok(ShadowedName, "rsi(14) > level", parameters: Params(("level", 55))),
    };

    public static TheoryData<string> FailingLabels { get; } = [.. Failing.Keys];

    public static TheoryData<string> PassingLabels { get; } = [.. Passing.Keys];

    /// <summary>The failing examples that have a problem with a suggestion.</summary>
    public static TheoryData<string> SuggestingLabels { get; } = [.. Failing.Where(c => c.Value.Problems.Any(p => p.Suggestion is not null)).Select(c => c.Key)];
}
