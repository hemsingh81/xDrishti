namespace XDrishti.Strategies.Tests.Lint;

/// <summary>The exact messages of the failing examples of <see cref="LintCases"/>, in the order of their problems.</summary>
internal static class LintMessages
{
    private static string Window(string comparison, string window, string suggestion) =>
        $"`{comparison}` can never be true: the window of `{window}` includes the current bar, so it already holds the other price. To compare with the previous bars write `{suggestion}`.";

    private static string Range(string comparison, string feature, string bounds, string suggestion) =>
        $"`{comparison}` can never be true: {feature} is {bounds}, so the rule never fires. The nearest comparison that can be true is `{suggestion}`.";

    private static string Context(string feature) => $"`{feature}` reads the daily market context: create the frame with a MarketContext (FrameOptions.Context).";

    public static readonly IReadOnlyDictionary<string, string[]> Cases = new Dictionary<string, string[]>
    {
        ["unknown_name: a typo of a feature"] =
        [
            "`closee` is not a parameter, a function, a constant or a feature. Did you mean `close`?",
        ],
        ["unknown_name: a typo of a function"] =
        [
            "`rsii` is not a function or a feature. Did you mean `rsi`?",
        ],
        ["unknown_name: the wrong case"] =
        [
            "`RSI` is not a function or a feature. Did you mean `rsi`?",
        ],
        ["unknown_name: a constant in lower case"] =
        [
            "`mon` is not a parameter, a function, a constant or a feature. Did you mean `MON`?",
        ],
        ["unknown_name: a name with no neighbour"] =
        [
            "`banana` is not a parameter, a function, a constant or a feature.",
        ],
        ["unknown_name: a typo of a parameter"] =
        [
            "`rsi_lne` is not a parameter, a function, a constant or a feature. Did you mean `rsi_len`?",
        ],
        ["unknown_name: a parameter that is not given"] =
        [
            "`rsi_len` is not a parameter, a function, a constant or a feature.",
        ],
        ["unknown_name: a typo of a scope"] =
        [
            "`todya` is not a scope; the dotted names are prev_day.open|high|low|close|volume, today.open|high|low and index.trend|adx. Did you mean `today`?",
        ],
        ["unknown_name: a typo after a scope"] =
        [
            "`prev_day.hgih` is not a name: after `prev_day.` comes open, high, low, close or volume. Did you mean `prev_day.high`?",
        ],
        ["unknown_name: a dotted name that is spelled with an underscore"] =
        [
            "`index.vol_pct` is not a name: after `index.` comes trend or adx. Did you mean `index_vol_pct`?",
        ],
        ["unknown_name: a regime name in lower case"] =
        [
            "`\"up_low_vol\"` is not a market regime; the names are UP_LOW_VOL, UP_NORMAL_VOL, UP_HIGH_VOL, RANGE_LOW_VOL, RANGE_NORMAL_VOL, RANGE_HIGH_VOL, DOWN_LOW_VOL, DOWN_NORMAL_VOL and DOWN_HIGH_VOL. Did you mean `\"UP_LOW_VOL\"`?",
        ],
        ["unknown_name: a function with a timeframe"] =
        [
            "`crosses_above` is a function of the language, not a feature, so it has no timeframe: only a feature is written tf(\"…\").name. Functions take the features as arguments, like crosses_above(tf(\"1h\").close, vwap).",
        ],
        ["unknown_name: a parameter with a timeframe"] =
        [
            "`rsi_len` is a strategy parameter, not a feature, so it has no timeframe: only a feature is written tf(\"…\").name. Functions take the features as arguments, like crosses_above(tf(\"1h\").close, vwap).",
        ],
        ["unknown_name: unknown names inside an unknown call"] =
        [
            "`banana` is not a function or a feature.",
            "`closee` is not a parameter, a function, a constant or a feature. Did you mean `close`?",
        ],
        ["bad_arguments: below the range"] =
        [
            "`rsi(0)`: 'n' must be between 1 and 10000. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: above the range"] =
        [
            "`rsi(10001)`: 'n' must be between 1 and 10000. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: not a whole number"] =
        [
            "`rsi(14.5)`: 'n' must be a whole number. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: one argument too many"] =
        [
            "`rsi` takes at most 1 argument, but 2 were given. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: several arguments too many"] =
        [
            "`or_low` takes at most 1 argument, but 3 were given. `or_low(minutes)` takes `minutes` (a whole number from 1 to 375, default 15).",
        ],
        ["bad_arguments: a feature with parameters written without parentheses"] =
        [
            "`rsi` has parameters, so it is written with parentheses: rsi() uses the defaults. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: fast not below slow"] =
        [
            "`macd_line(26, 12)`: 'fast' must be below 'slow'. `macd_line(fast, slow, signal, source=close)` takes `fast` (a whole number from 1 to 10000, default 12), `slow` (a whole number from 2 to 10000, default 26), `signal` (a whole number from 1 to 10000, default 9) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: a band width of zero"] =
        [
            "`bb_upper(25, 0)`: 'k' must be above 0. `bb_upper(n, k, source=close)` takes `n` (a whole number from 1 to 10000, default 20), `k` (a number from 0 to 100, default 2) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: an opening range that is not a whole number of bars"] =
        [
            "`or_high(7)`: The opening range must be a whole number of 15m bars. `or_high(minutes)` takes `minutes` (a whole number from 1 to 375, default 15).",
        ],
        ["bad_arguments: a default that the timeframe refuses"] =
        [
            "`or_high()`: The opening range must be a whole number of 30m bars. `or_high(minutes)` takes `minutes` (a whole number from 1 to 375, default 15).",
        ],
        ["bad_arguments: an argument that is not a number"] =
        [
            "Argument 1 (`n`) of `rsi` must be a number, but `close > 1` is a true/false value. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: an argument that changes with the data"] =
        [
            "Argument 1 (`n`) of `rsi` must be a number known when the condition is compiled (a number or a strategy parameter), but `close` changes from bar to bar. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: a source that is not a price"] =
        [
            "`volume` is not a price source: `source` is close, open, high or low.",
        ],
        ["bad_arguments: a typo of a source"] =
        [
            "`clsoe` is not a price source: `source` is close, open, high or low. Did you mean `close`?",
        ],
        ["bad_arguments: a source for a feature that reads the whole bar"] =
        [
            "`atr` reads the whole bar, so it has no `source`. `atr(n)` takes `n` (a whole number from 1 to 10000, default 14).",
        ],
        ["bad_arguments: the source given twice"] =
        [
            "`source` is given twice.",
        ],
        ["bad_arguments: a named argument that is not the source"] =
        [
            "`ema` has no argument named `n`; the only named argument is `source`. `ema(n, source=close)` takes `n` (a whole number from 1 to 10000, default 20) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: a function written without parentheses"] =
        [
            "`abs` is a function: call it with parentheses and arguments, like abs(x).",
        ],
        ["bad_arguments: too few arguments for a function"] =
        [
            "`crosses_above(a, b)` takes 2 arguments, but 1 was given.",
        ],
        ["bad_arguments: too many arguments for a function"] =
        [
            "`between(x, low, high)` takes 3 arguments, but 4 were given.",
        ],
        ["bad_arguments: a named argument for a function"] =
        [
            "`abs` takes no named arguments (abs(x)).",
        ],
        ["bad_arguments: a parameter called like a function"] =
        [
            "`rsi_len` is a strategy parameter (a number), not a function; it cannot be called.",
        ],
        ["bad_arguments: the time called like a function"] =
        [
            "`time` is not a function; write it without parentheses.",
        ],
        ["bad_arguments: a count that changes with the data"] =
        [
            "The count of `rising` must be a number known when the condition is compiled (a number or a strategy parameter), but `close` changes from bar to bar.",
        ],
        ["bad_arguments: a count that is not finite"] =
        [
            "The count of `rising` must be a finite number, but `1 / 0` is NaN.",
        ],
        ["bad_arguments: an argument that is not finite"] =
        [
            "Argument 1 (`n`) of `rsi` must be a finite number, but `1 / 0` is NaN. `rsi(n, source=close)` takes `n` (a whole number from 1 to 10000, default 14) and `source` (close, open, high or low, default close).",
        ],
        ["bad_arguments: tf without a feature"] =
        [
            "`tf` qualifies a feature with a timeframe: write tf(\"1h\").close or tf(\"1h\").ema(20).",
        ],
        ["bad_arguments: a qualified feature with parameters written without parentheses"] =
        [
            "`tf(\"1h\").ema` has parameters, so it is written with parentheses: ema() uses the defaults. `ema(n, source=close)` takes `n` (a whole number from 1 to 10000, default 20) and `source` (close, open, high or low, default close).",
        ],
        ["unsupported_timeframe: 1d in lower case"] =
        [
            "`\"1d\"` is not a timeframe: tf(\"…\") takes 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M (the case matters: 1m is a minute, 1M a month). Did you mean `\"1D\"`?",
        ],
        ["unsupported_timeframe: 1H in upper case"] =
        [
            "`\"1H\"` is not a timeframe: tf(\"…\") takes 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M (the case matters: 1m is a minute, 1M a month). Did you mean `\"1h\"`?",
        ],
        ["unsupported_timeframe: a timeframe near a real one"] =
        [
            "`\"2h\"` is not a timeframe: tf(\"…\") takes 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M (the case matters: 1m is a minute, 1M a month). Did you mean `\"1h\"`?",
        ],
        ["unsupported_timeframe: an empty timeframe"] =
        [
            "`\"\"` is not a timeframe: tf(\"…\") takes 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M (the case matters: 1m is a minute, 1M a month).",
        ],
        ["unsupported_timeframe: vwap on 1D by tf"] =
        [
            "'vwap' needs intraday bars; it cannot be computed on 1D. `vwap` runs on 1m, 5m, 10m, 15m, 30m, 1h.",
        ],
        ["unsupported_timeframe: vwap on a daily strategy"] =
        [
            "'vwap' needs intraday bars; it cannot be computed on 1D. `vwap` runs on 1m, 5m, 10m, 15m, 30m, 1h (the strategy runs on 1D; write tf(\"…\") to read it on another).",
        ],
        ["unsupported_timeframe: the previous day on a weekly strategy"] =
        [
            "'prev_day_high' is a value of the trading session; it cannot be computed on 1W (read it on 1m or 1D). `prev_day_high` runs on 1m, 5m, 10m, 15m, 30m, 1h, 1D (the strategy runs on 1W; write tf(\"…\") to read it on another).",
        ],
        ["unsupported_timeframe: today's value on 1W by tf"] =
        [
            "'today_open' is a value of the trading session; it cannot be computed on 1W (read it on 1m or 1D). `today_open` runs on 1m, 5m, 10m, 15m, 30m, 1h, 1D.",
        ],
        ["unsupported_timeframe: a date feature on 1h"] =
        [
            "'vix' changes once per date; it can only be asked for on 1m (the default), not on 1h. `vix` runs on 1m.",
        ],
        ["negative_or_zero_lag: a subscript of zero"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `close[0]` is just `close`.",
        ],
        ["negative_or_zero_lag: a subscript of minus zero"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `close[-0]` is just `close`.",
        ],
        ["negative_or_zero_lag: a subscript of two zeros"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `close[00]` is just `close`.",
        ],
        ["negative_or_zero_lag: a subscript of zero on a dotted feature"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `prev_day.high[0]` is just `prev_day.high`.",
        ],
        ["negative_or_zero_lag: a subscript of zero on a qualified feature"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `tf(\"1h\").close[0]` is just `tf(\"1h\").close`.",
        ],
        ["negative_or_zero_lag: a subscript of zero on an unknown name has no suggestion"] =
        [
            "`closee` is not a parameter, a function, a constant or a feature. Did you mean `close`?",
            "A subscript counts closed bars back and must be a whole number from 1 up; `closee[0]` is just `closee`.",
        ],
        ["negative_or_zero_lag: a subscript of zero on a feature of a timeframe that does not exist"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `tf(\"2h\").close[0]` is just `tf(\"2h\").close`.",
            "`\"2h\"` is not a timeframe: tf(\"…\") takes 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M (the case matters: 1m is a minute, 1M a month). Did you mean `\"1h\"`?",
        ],
        ["negative_or_zero_lag: a subscript of zero on a name that is not a feature"] =
        [
            "A subscript reads a feature some closed bars ago, but `rsi_len` is a strategy parameter, not a feature. Use crosses_above, crosses_below, rising or falling to compare with an earlier value, or write the feature twice.",
            "A subscript counts closed bars back and must be a whole number from 1 up; `rsi_len[0]` is just `rsi_len`.",
        ],
        ["negative_or_zero_lag: a negative subscript"] =
        [
            "A subscript counts closed bars back and must be a whole number from 1 up; `-1` would read the future.",
        ],
        ["negative_or_zero_lag: a rising count of zero"] =
        [
            "The count of `rising` is the number of closed bars to look back and must be a whole number from 1 up, but it is 0.",
        ],
        ["negative_or_zero_lag: a rising count that is not whole"] =
        [
            "The count of `rising` is the number of closed bars to look back and must be a whole number from 1 up, but it is 1.5.",
        ],
        ["negative_or_zero_lag: a negative falling count"] =
        [
            "The count of `falling` is the number of closed bars to look back and must be a whole number from 1 up, but it is -2.",
        ],
        ["negative_or_zero_lag: a count from a parameter"] =
        [
            "The count of `rising` is the number of closed bars to look back and must be a whole number from 1 up, but it is 0.",
        ],
        ["lag_too_deep: a subscript of 256"] =
        [
            "`close[256]` needs a history of 257 closed bars, but the frame keeps at most 256 (lags 0 to 255).",
        ],
        ["lag_too_deep: a huge subscript"] =
        [
            "`close[100000]` needs a history of 100001 closed bars, but the frame keeps at most 256 (lags 0 to 255).",
        ],
        ["lag_too_deep: a rising count of 256"] =
        [
            "`rising(close, 256)` reads values up to 256 closed bars back (the lags add up), so it needs a history of 257; the frame keeps at most 256 (lags 0 to 255).",
        ],
        ["lag_too_deep: lags that add up inside rising"] =
        [
            "`rising(close[200], 100)` reads values up to 300 closed bars back (the lags add up), so it needs a history of 301; the frame keeps at most 256 (lags 0 to 255).",
        ],
        ["lag_too_deep: the extra lag of a crossing"] =
        [
            "`crosses_above(close[255], vwap)` reads values up to 256 closed bars back (the lags add up), so it needs a history of 257; the frame keeps at most 256 (lags 0 to 255).",
        ],
        ["lag_too_deep: the extra lag of a downward crossing"] =
        [
            "`crosses_below(close, vwap[255])` reads values up to 256 closed bars back (the lags add up), so it needs a history of 257; the frame keeps at most 256 (lags 0 to 255).",
            "`crosses_below(close, vwap[255])` reads `vwap` up to 256 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_too_deep: two places"] =
        [
            "`close[300]` needs a history of 301 closed bars, but the frame keeps at most 256 (lags 0 to 255).",
            "`close[400]` needs a history of 401 closed bars, but the frame keeps at most 256 (lags 0 to 255).",
        ],
        ["lag_too_deep: a type error in a side does not hide the depth of a crossing"] =
        [
            "`crosses_above(close[255] + true, vwap)` reads values up to 256 closed bars back (the lags add up), so it needs a history of 257; the frame keeps at most 256 (lags 0 to 255).",
            "The right side of `+` needs a number, but `true` is a true/false value.",
        ],
        ["lag_too_deep: a type error in the operand does not hide the depth of rising"] =
        [
            "`rising(close[250] + true, 10)` reads values up to 260 closed bars back (the lags add up), so it needs a history of 261; the frame keeps at most 256 (lags 0 to 255).",
            "The right side of `+` needs a number, but `true` is a true/false value.",
        ],
        ["lag_on_non_feature: a parameter"] =
        [
            "A subscript reads a feature some closed bars ago, but `rsi_len` is a strategy parameter, not a feature. Use crosses_above, crosses_below, rising or falling to compare with an earlier value, or write the feature twice.",
        ],
        ["lag_on_non_feature: a constant"] =
        [
            "A subscript reads a feature some closed bars ago, but `MON` is a constant, not a feature. Use crosses_above, crosses_below, rising or falling to compare with an earlier value, or write the feature twice.",
        ],
        ["lag_on_non_feature: the time"] =
        [
            "A subscript reads a feature some closed bars ago, but `time` is the time of day, not a feature. Use crosses_above, crosses_below, rising or falling to compare with an earlier value, or write the feature twice.",
        ],
        ["mixed_timeframes_in_lag: a crossing of the close and a daily average"] =
        [
            "`crosses_above(close, tf(\"1D\").ema(20))` mixes the timeframes 15m and 1D: the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `close > tf(\"1D\").ema(20) and close[1] <= tf(\"1D\").ema(20)`.",
        ],
        ["mixed_timeframes_in_lag: a downward crossing where only the second side can be lagged by hand"] =
        [
            "`crosses_below(tf(\"1h\").ema(20), close)` mixes the timeframes 1h and 15m: the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `tf(\"1h\").ema(20) < close and tf(\"1h\").ema(20) >= close[1]`.",
        ],
        ["mixed_timeframes_in_lag: a crossing of two calls has no explicit form"] =
        [
            "`crosses_below(tf(\"1h\").ema(20), ema(50))` mixes the timeframes 1h and 15m: the previous value of each side would be a different moment. Compare on one timeframe.",
        ],
        ["mixed_timeframes_in_lag: a sum of two timeframes in rising"] =
        [
            "`rising(close + tf(\"1h\").close, 2)` mixes the timeframes 15m and 1h: the previous value of each side would be a different moment. Compare on one timeframe.",
        ],
        ["mixed_timeframes_in_lag: a difference of two timeframes in falling"] =
        [
            "`falling(close - tf(\"1D\").close, 1)` mixes the timeframes 15m and 1D: the previous value of each side would be a different moment. Compare on one timeframe.",
        ],
        ["mixed_timeframes_in_lag: a feature of the context against a bar feature"] =
        [
            "`crosses_above(close, vix)` mixes the timeframes 15m and the date (calendar and context features): the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `close > vix and close[1] <= vix`.",
        ],
        ["mixed_timeframes_in_lag: the context against a bar feature on a 1m strategy"] =
        [
            "`crosses_above(close, vix)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `close > vix and close[1] <= vix`.",
        ],
        ["mixed_timeframes_in_lag: the context first on a 1m strategy lags the bar feature by hand"] =
        [
            "`crosses_below(vix, close)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `vix < close and vix >= close[1]`.",
        ],
        ["mixed_timeframes_in_lag: the index trend against a bar feature on a 1m strategy"] =
        [
            "`crosses_above(close, index.trend)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `close > index.trend and close[1] <= index.trend`.",
        ],
        ["mixed_timeframes_in_lag: a product of a bar feature and the context in rising on a 1m strategy"] =
        [
            "`rising(close * vix, 2)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. A calendar or context feature changes once a date, so compare it without a lag, or lag only the bar side by hand.",
        ],
        ["mixed_timeframes_in_lag: a calendar feature against a bar feature in falling on a 1m strategy"] =
        [
            "`falling(close - day_of_week, 1)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. A calendar or context feature changes once a date, so compare it without a lag, or lag only the bar side by hand.",
        ],
        ["mixed_timeframes_in_lag: a 1m bar feature written with tf against the context on a 15m strategy"] =
        [
            "`crosses_above(tf(\"1m\").close, vix)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. A calendar or context feature changes once a date, so compare it without a lag, or lag only the bar side by hand.",
        ],
        ["mixed_timeframes_in_lag: a 1m bar feature written with tf against the context on a daily strategy"] =
        [
            "`crosses_above(tf(\"1m\").close, vix)` mixes the timeframes 1m and the date (calendar and context features): the previous value of each side would be a different moment. A calendar or context feature changes once a date, so compare it without a lag, or lag only the bar side by hand.",
        ],
        ["mixed_timeframes_in_lag: the context against a bar feature on a daily strategy"] =
        [
            "`crosses_below(close, breadth)` mixes the timeframes 1D and the date (calendar and context features): the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `close < breadth and close[1] >= breadth`.",
        ],
        ["mixed_timeframes_in_lag: two bar timeframes and the context"] =
        [
            "`rising(close + tf(\"1h\").close - vix, 2)` mixes the timeframes 15m, 1h and the date (calendar and context features): the previous value of each side would be a different moment. A calendar or context feature changes once a date, so compare it without a lag, or lag only the bar side by hand.",
        ],
        ["mixed_timeframes_in_lag: only the second crossing mixes"] =
        [
            "`crosses_above(close, tf(\"1W\").close)` mixes the timeframes 15m and 1W: the previous value of each side would be a different moment. Compare on one timeframe, or say what you mean: `close > tf(\"1W\").close and close[1] <= tf(\"1W\").close`.",
        ],
        ["mixed_timeframes_in_lag: a first side that already has a subscript cannot be lagged by hand"] =
        [
            "`crosses_above(close[1], tf(\"1D\").close)` mixes the timeframes 15m and 1D: the previous value of each side would be a different moment. Compare on one timeframe.",
        ],
        ["mixed_timeframes_in_lag: a second side that already has a subscript cannot be lagged by hand"] =
        [
            "`crosses_below(tf(\"1h\").close, close[2])` mixes the timeframes 1h and 15m: the previous value of each side would be a different moment. Compare on one timeframe.",
        ],
        ["mixed_timeframes_in_lag: a count that is not whole does not hide the mix in rising"] =
        [
            "`rising(close + tf(\"1h\").close, 0.5)` mixes the timeframes 15m and 1h: the previous value of each side would be a different moment. Compare on one timeframe.",
            "The count of `rising` is the number of closed bars to look back and must be a whole number from 1 up, but it is 0.5.",
        ],
        ["mixed_timeframes_in_lag: a count that is not finite does not hide the mix in falling"] =
        [
            "`falling(close - tf(\"1D\").close, 1 / 0)` mixes the timeframes 15m and 1D: the previous value of each side would be a different moment. Compare on one timeframe.",
            "The count of `falling` must be a finite number, but `1 / 0` is NaN.",
        ],
        ["mixed_timeframes_in_lag: a type error in one side does not hide the mix in a crossing"] =
        [
            "`crosses_above(close + true, tf(\"1h\").ema(20))` mixes the timeframes 15m and 1h: the previous value of each side would be a different moment. Compare on one timeframe.",
            "The right side of `+` needs a number, but `true` is a true/false value.",
        ],
        ["mixed_timeframes_in_lag: a type error in the operand does not hide the mix in rising"] =
        [
            "`rising(close + true + tf(\"1h\").close, 2)` mixes the timeframes 15m and 1h: the previous value of each side would be a different moment. Compare on one timeframe.",
            "The right side of `+` needs a number, but `true` is a true/false value.",
        ],
        ["lag_across_session: the first subscript that a 5m session cannot hold"] =
        [
            "`vwap[75]` reads `vwap` up to 75 closed bars back, but a session holds at most 75 bars of 5m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 74 bars back.",
        ],
        ["lag_across_session: the first count that an hourly session cannot hold"] =
        [
            "`rising(vwap, 7)` reads `vwap` up to 7 closed bars back, but a session holds at most 7 bars of 1h and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 6 bars back.",
        ],
        ["lag_across_session: the first count that a 5m session cannot hold"] =
        [
            "`rising(vwap, 75)` reads `vwap` up to 75 closed bars back, but a session holds at most 75 bars of 5m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 74 bars back.",
        ],
        ["lag_across_session: the first count that a 15m session cannot hold"] =
        [
            "`falling(today_low, 25)` reads `today_low` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: a subscript on 1m at the whole session"] =
        [
            "`vwap[375]` needs a history of 376 closed bars, but the frame keeps at most 256 (lags 0 to 255).",
            "`vwap[375]` reads `vwap` up to 375 closed bars back, but a session holds at most 375 bars of 1m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 374 bars back.",
        ],
        ["lag_across_session: a 5m feature read on a daily strategy"] =
        [
            "`tf(\"5m\").vwap[80]` reads `tf(\"5m\").vwap` up to 80 closed bars back, but a session holds at most 75 bars of 5m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 74 bars back.",
        ],
        ["lag_across_session: a time check does not clear a read that is never known"] =
        [
            "`rising(vwap, 25)` reads `vwap` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: the extra lag of a crossing counts"] =
        [
            "`crosses_above(close, vwap[74])` reads `vwap` up to 75 closed bars back, but a session holds at most 75 bars of 5m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 74 bars back.",
        ],
        ["lag_across_session: the lags of a subscript and of rising add up"] =
        [
            "`rising(vwap[20], 5)` reads `vwap` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: two features in one call give one warning"] =
        [
            "`rising(vwap - today_open, 25)` reads `vwap`, `today_open` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: two calls give two warnings"] =
        [
            "`rising(vwap, 25)` reads `vwap` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
            "`rising(today_low, 30)` reads `today_low` up to 30 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: a feature of another timeframe"] =
        [
            "`rising(tf(\"1h\").vwap, 7)` reads `tf(\"1h\").vwap` up to 7 closed bars back, but a session holds at most 7 bars of 1h and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 6 bars back.",
        ],
        ["lag_across_session: an opening range"] =
        [
            "`rising(or_high(15), 25)` reads `or_high(15)` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: the previous day's value"] =
        [
            "`rising(prev_day.high, 30)` reads `prev_day.high` up to 30 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
        ],
        ["lag_across_session: a type error in an operand does not hide it"] =
        [
            "`rising(vwap + true, 25)` reads `vwap` up to 25 closed bars back, but a session holds at most 25 bars of 15m and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most 24 bars back.",
            "The right side of `+` needs a number, but `true` is a true/false value.",
        ],
        ["time_on_daily: a daily strategy"] =
        [
            "`time` is the minute at which the evaluated bar closed, and a 1D bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["time_on_daily: a weekly strategy"] =
        [
            "`time` is the minute at which the evaluated bar closed, and a 1W bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["time_on_daily: a monthly strategy"] =
        [
            "`time` is the minute at which the evaluated bar closed, and a 1M bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["time_on_daily: the time further in the condition"] =
        [
            "`time` is the minute at which the evaluated bar closed, and a 1D bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["time_on_daily: a text that is not a time does not hide it"] =
        [
            "`time` is the minute at which the evaluated bar closed, and a 1D bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
            "`\"9:45\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["time_on_daily: a number to compare the time with does not hide it"] =
        [
            "`>` cannot compare `time` (the time of day) with `5` (a number). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
            "`time` is the minute at which the evaluated bar closed, and a 1D bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["time_on_daily: a number before the time does not hide it"] =
        [
            "`<` cannot compare `5` (a number) with `time` (the time of day). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
            "`time` is the minute at which the evaluated bar closed, and a 1D bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["time_on_daily: a subscript on the time does not hide it"] =
        [
            "A subscript reads a feature some closed bars ago, but `time` is the time of day, not a feature. Use crosses_above, crosses_below, rising or falling to compare with an earlier value, or write the feature twice.",
            "`time` is the minute at which the evaluated bar closed, and a 1D bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
        ],
        ["bad_time_literal: one digit for the hour"] =
        [
            "`\"9:45\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["bad_time_literal: before the open"] =
        [
            "`\"09:00\"` is outside the session: `time` is the minute at which a bar closes, from 09:15 to 15:30.",
        ],
        ["bad_time_literal: after the close"] =
        [
            "`\"15:31\"` is outside the session: `time` is the minute at which a bar closes, from 09:15 to 15:30.",
        ],
        ["bad_time_literal: an hour that does not exist"] =
        [
            "`\"25:00\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["bad_time_literal: with seconds"] =
        [
            "`\"09:45:00\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["bad_time_literal: a dot for the colon"] =
        [
            "`\"09.45\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["bad_time_literal: an empty text"] =
        [
            "`\"\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["bad_time_literal: the text before the time"] =
        [
            "`\"abc\"` is not a time of day in the form HH:MM (two digits, a colon, two digits), like \"09:45\".",
        ],
        ["type_mismatch: a condition that is a number"] =
        [
            "A condition must be true or false, but `rsi(14)` is a number. Compare it with something, like rsi(14) > 55.",
        ],
        ["type_mismatch: and of a number"] =
        [
            "The left side of `and` needs a true/false value, but `rsi(14)` is a number. Compare it with something, like rsi(14) > 55.",
        ],
        ["type_mismatch: or of a number on the right"] =
        [
            "The right side of `or` needs a true/false value, but `rsi(14)` is a number. Compare it with something, like rsi(14) > 55.",
        ],
        ["type_mismatch: and of the time"] =
        [
            "The left side of `and` needs a true/false value, but `time` is the time of day. Compare `time` with a text, like time >= \"09:45\".",
        ],
        ["type_mismatch: not of a regime"] =
        [
            "`not` needs a true/false value, but `regime()` is a market regime. Compare it with a regime name, like regime() == \"UP_LOW_VOL\".",
        ],
        ["type_mismatch: the time against a number"] =
        [
            "`>` cannot compare `time` (the time of day) with `5` (a number). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: a number against a true/false value"] =
        [
            "`>` cannot compare `close` (a number) with `true` (a true/false value). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: ordering a regime"] =
        [
            "`<` cannot compare `regime()` (a market regime) with `3` (a number). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: a regime code that does not exist"] =
        [
            "`10` is not a regime code: `regime()` is compared with a regime name or a code from 1 (UP_LOW_VOL) to 9 (DOWN_HIGH_VOL).",
        ],
        ["type_mismatch: not of a number"] =
        [
            "`not` needs a true/false value, but `close` is a number. Compare it with something, like rsi(14) > 55.",
        ],
        ["type_mismatch: minus of a true/false value"] =
        [
            "`-` needs a number, but `true` is a true/false value.",
        ],
        ["type_mismatch: two texts"] =
        [
            "`==` cannot compare `\"UP\"` (a text) with `\"UP\"` (a text). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: two regimes"] =
        [
            "`==` cannot compare `regime()` (a market regime) with `regime()` (a market regime). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: ordering true/false values"] =
        [
            "`<` cannot compare `true` (a true/false value) with `false` (a true/false value). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: a condition that is a text"] =
        [
            "A condition must be true or false, but `\"09:45\"` is a text. A text can only be compared with `time` or `regime()`.",
        ],
        ["type_mismatch: a condition that is the time"] =
        [
            "A condition must be true or false, but `time` is the time of day. Compare `time` with a text, like time >= \"09:45\".",
        ],
        ["type_mismatch: a sum with a true/false value"] =
        [
            "The right side of `+` needs a number, but `true` is a true/false value.",
        ],
        ["type_mismatch: a regime against a feature"] =
        [
            "`==` cannot compare `regime()` (a market regime) with `close` (a number). A comparison takes two numbers, two true/false values (only == and !=), `time` and a \"HH:MM\" text, or `regime()` and a regime name or a code from 1 to 9 (only == and !=).",
        ],
        ["type_mismatch: a parameter that is not a number"] =
        [
            "The strategy parameter `bad` is NaN, not a finite number; parameters are numbers.",
        ],
        ["type_mismatch: a regime in arithmetic"] =
        [
            "Argument 1 (`x`) of `abs` needs a number, but `regime()` is a market regime. Compare it with a regime name, like regime() == \"UP_LOW_VOL\".",
        ],
        ["constant_condition: one less than two"] =
        [
            "`1 < 2` reads no data, so it is the same on every bar.",
        ],
        ["constant_condition: true"] =
        [
            "`true` reads no data, so it is the same on every bar.",
        ],
        ["constant_condition: a crossing of constants"] =
        [
            "`crosses_above(1, 2)` reads no data, so it is the same on every bar.",
        ],
        ["constant_condition: not false"] =
        [
            "`not false` reads no data, so it is the same on every bar.",
        ],
        ["constant_condition: one constant part among data"] =
        [
            "`2 > 1` reads no data, so it is the same on every bar.",
        ],
        ["constant_condition: a parameter against a number"] =
        [
            "`level > 50` reads no data, so it is the same on every bar.",
        ],
        ["constant_condition: between constants"] =
        [
            "`between(5, 1, 10)` reads no data, so it is the same on every bar.",
        ],
        ["never_true: close above its own window"] = [Window("close > highest(20)", "highest(20)", "close > highest(20)[1]")],
        ["never_true: high above its own window"] = [Window("high > highest(20)", "highest(20)", "high > highest(20)[1]")],
        ["never_true: close above the highest close"] = [Window("close > highest(20, source=close)", "highest(20, source=close)", "close > highest(20, source=close)[1]")],
        ["never_true: a crossing of the own window"] = [Window("crosses_above(close, highest(20))", "highest(20)", "crosses_above(close, highest(20)[1])")],
        ["never_true: low below its own window"] = [Window("low < lowest(20)", "lowest(20)", "low < lowest(20)[1]")],
        ["never_true: close below the lowest close"] = [Window("close < lowest(20, source=close)", "lowest(20, source=close)", "close < lowest(20, source=close)[1]")],
        ["never_true: the window written first"] = [Window("highest(20) < close", "highest(20)", "highest(20)[1] < close")],
        ["never_true: rsi above 100"] = [Range("rsi(14) > 100", "rsi", "between 0 and 100", "rsi(14) >= 100")],
        ["never_true: rsi below 0"] = [Range("rsi(14) < 0", "rsi", "between 0 and 100", "rsi(14) <= 0")],
        ["never_true: adx above 100"] = [Range("adx(14) > 100", "adx", "between 0 and 100", "adx(14) >= 100")],
        ["never_true: volume below 0"] = [Range("volume < 0", "volume", "at least 0", "volume <= 0")],
        ["never_true: regime volatility above 2"] = [Range("regime_vol > 2", "regime_vol", "between 0 and 2", "regime_vol >= 2"), Context("regime_vol")],
        ["never_true: supertrend direction above 1"] = [Range("supertrend_dir(10, 3) > 1", "supertrend_dir", "between -1 and 1", "supertrend_dir(10, 3) >= 1")],
        ["never_true: breadth above 100"] = [Range("breadth > 100", "breadth", "between 0 and 100", "breadth >= 100"), Context("breadth")],
        ["never_true: day of week above 7"] = [Range("day_of_week > 7", "day_of_week", "between 1 and 7", "day_of_week >= 7")],
        ["never_true: a crossing of a bounded oscillator has no suggestion"] =
        [
            "`crosses_above(rsi(14), 100)` can never be true: rsi is between 0 and 100, so the rule never fires. Remove the condition or change the number.",
        ],
        ["never_true: the number written first"] = [Range("0 > rsi(14)", "rsi", "between 0 and 100", "0 >= rsi(14)")],
        ["never_true: a parameter that decides"] = [Range("rsi(14) > rsi_hi", "rsi", "between 0 and 100", "rsi(14) >= 100")],
        ["never_true: a bound that cannot be reached has no suggestion"] =
        [
            "`gap_pct < -100` can never be true: gap_pct is more than -100, so the rule never fires. Remove the condition or change the number.",
        ],
        ["redundant_timeframe: the strategy's own timeframe"] =
        [
            "`tf(\"15m\")` is redundant: the strategy already runs on 15m. Write `ema(20)`.",
        ],
        ["redundant_timeframe: an hourly strategy"] =
        [
            "`tf(\"1h\")` is redundant: the strategy already runs on 1h. Write `close`.",
        ],
        ["redundant_timeframe: a feature of the day on 1m"] =
        [
            "`tf(\"1m\")` is redundant: the strategy already runs on 1m. Write `vix`.",
            "`vix` reads the daily market context: create the frame with a MarketContext (FrameOptions.Context).",
        ],
        ["needs_context: the regime"] =
        [
            "`regime` reads the daily market context: create the frame with a MarketContext (FrameOptions.Context).",
        ],
        ["needs_context: two context features give one note"] =
        [
            "`vix` reads the daily market context: create the frame with a MarketContext (FrameOptions.Context).",
        ],
        ["needs_expiry_days: the expiry day"] =
        [
            "`is_expiry_day` reads the expiry dates: create the frame with the expiry dates (FrameOptions.ExpiryDays; an empty set is allowed).",
        ],
        ["needs_expiry_days: both notes in source order"] =
        [
            "`index_trend` reads the daily market context: create the frame with a MarketContext (FrameOptions.Context).",
            "`is_expiry_day` reads the expiry dates: create the frame with the expiry dates (FrameOptions.ExpiryDays; an empty set is allowed).",
        ],
        ["shadowed_name: a parameter named like a feature"] =
        [
            "The strategy parameter `close` has the name of the feature `close` and wins over it: here `close` is the parameter's value. Rename the parameter to read the feature `close`.",
        ],
        ["shadowed_name: a parameter named like a constant"] =
        [
            "The strategy parameter `MON` has the name of the constant `MON` and wins over it: here `MON` is the parameter's value. Rename the parameter to read the constant `MON`.",
        ],
        ["shadowed_name: a parameter named like a function"] =
        [
            "The strategy parameter `max` has the name of the function `max` and wins over it: here `max` is the parameter's value. Rename the parameter to read the function `max`.",
        ],
    };
}
