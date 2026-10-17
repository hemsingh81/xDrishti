namespace XDrishti.Strategies.Lint;

/// <summary>
/// The stable codes of the problems the binder and the lint report (<c>strategy.lint.*</c>, the table of the spec P3-02 and of doc 11 §2), plus <see cref="ShadowedName"/>. A program switches on the code, a person
/// reads the message. Every problem has a span with a line and column; the rule for each span is written on its code. An <b>error</b> stops the compile; a <b>warning</b> and an <b>information</b> do not.
/// </summary>
public static class LintCodes
{
    /// <summary>
    /// An identifier or function that is not a strategy parameter, a builtin, a constant, <c>time</c> or a catalog feature; also a dotted pair that is not one of the DSL spellings, a regime name that is none of the
    /// nine, a builtin or a parameter written with <c>tf(…)</c>. The span is the name (the whole dotted pair when only its second half is wrong; the string for a regime name). The suggestion is the closest known
    /// name within edit distance 2 (case-insensitively, so <c>RSI</c> suggests <c>rsi</c>; distance 1 for a text of one or two letters); the message lists up to five when several are equally close, the one that differs
    /// least in case first. Error.
    /// </summary>
    public const string UnknownName = "strategy.lint.unknown_name";

    /// <summary>
    /// A wrong number, kind or range of arguments, or a name used the wrong way: too many arguments, an argument that is not a number known at compile time, a value the catalog refuses (outside its range, not a whole
    /// number, <c>fast</c> not below <c>slow</c>, an opening range that is not a whole number of bars), an unknown or repeated <c>source=</c>, a function written without parentheses, a feature with parameters written
    /// without them (<c>rsi</c>, not <c>rsi()</c>), a parameter or constant called like a function. The message quotes the catalog's parameter names and ranges. The span is the offending argument (for too many
    /// arguments, from the first extra argument to the last one: <c>3, 4</c> in <c>or_low(15, 3, 4)</c>; for too few, the whole call; the whole call or reference when nothing more precise exists). Error.
    /// </summary>
    public const string BadArguments = "strategy.lint.bad_arguments";

    /// <summary>
    /// A <c>tf("…")</c> that is none of <c>1m 5m 10m 15m 30m 1h 1D 1W 1M</c> (case as written; <c>1d</c> suggests <c>1D</c>), or a feature the catalog does not support on the timeframe (<c>vwap</c> on 1D,
    /// <c>prev_day_*</c> on 1W, a date-driven feature on anything but 1m). The message names the feature and the timeframe and lists the timeframes the feature supports. The span is the string of <c>tf("…")</c>, or the
    /// reference when the timeframe is the strategy's own. Error.
    /// </summary>
    public const string UnsupportedTimeframe = "strategy.lint.unsupported_timeframe";

    /// <summary>
    /// A subscript or <c>rising/falling</c> count that is not a whole number of at least 1 (<c>close[0]</c>, <c>close[-1]</c>, <c>rising(close, 0)</c>, <c>rising(close, 1.5)</c>). The span is the number, except for a subscript
    /// of zero (<c>close[0]</c>, <c>close[-0]</c>, <c>close[00]</c>), where it is the whole subscript, so that the suggestion (the feature without the subscript) replaces its own span; a subscript of a name that could
    /// not be bound (<c>closee[0]</c>) has no suggestion. A negative subscript has none either. Error.
    /// </summary>
    public const string NegativeOrZeroLag = "strategy.lint.negative_or_zero_lag";

    /// <summary>
    /// A condition that needs more history than the frame keeps: the largest lag it reads plus 1 is above 256 (<c>close[256]</c>, <c>rising(close, 256)</c>, <c>rising(close[200], 100)</c>, where the lags add up). The span is
    /// the subscripted feature when its own lag is too deep (<c>close[300]</c>), otherwise the <c>rising/falling</c> or <c>crosses_*</c> call. Error.
    /// </summary>
    public const string LagTooDeep = "strategy.lint.lag_too_deep";

    /// <summary>A subscript on something that is not a feature: a strategy parameter, a constant or <c>time</c> (a subscript after a call is a syntax error). The span is the thing that was subscripted. Error.</summary>
    public const string LagOnNonFeature = "strategy.lint.lag_on_non_feature";

    /// <summary>
    /// A <c>crosses_above/below</c>, <c>rising</c> or <c>falling</c> whose features are not all on one timeframe, so that "the previous value" of each side would be a different time. The span is the call; the message
    /// names the timeframes and, when one side is a plain reference, the explicit form that says what is meant (the suggestion). A feature of the date (calendar and context features) is a clock of its own, on any strategy timeframe;
    /// when it is one of the clocks and there is no explicit form, the message says that such a feature changes once a date, so it is compared without a lag or only the bar side is lagged by hand. Error.
    /// </summary>
    public const string MixedTimeframesInLag = "strategy.lint.mixed_timeframes_in_lag";

    /// <summary>
    /// A lagged read (a subscript, or the lags of <c>crosses_*</c>, <c>rising</c> and <c>falling</c>) of a feature that starts again at every session (<c>vwap</c>, <c>or_*</c>, <c>today_*</c>, <c>prev_day_*</c>,
    /// <c>gap_pct</c>, on 1m to 1h; ADR 0035) from further back than a whole session holds: <c>k</c> bars of its timeframe <c>T</c> with <c>k · T</c> at least the 375 minutes of a session (<c>vwap[375]</c> on 1m,
    /// <c>rising(vwap, 7)</c> on 1h, <c>tf("5m").vwap[80]</c> on a 1D strategy). The frame keeps such a feature's history for the current session only, so every bar that far back is in an earlier session and the read is
    /// never known (unknown is not true). A shorter read that reaches before the session's first bars is unknown on those bars only and is not warned. The strategy's own timeframe does not matter. The span is the call, or
    /// the subscripted feature; the message names the features, how far back, and the most that a session holds. Warning.
    /// </summary>
    public const string LagAcrossSession = "strategy.lint.lag_across_session";

    /// <summary><c>time</c> used on a 1D, 1W or 1M strategy, where a bar has no time of day. The span is the keyword. Error.</summary>
    public const string TimeOnDaily = "strategy.lint.time_on_daily";

    /// <summary>A text compared with <c>time</c> that is not <c>HH:MM</c> (two digits, a colon, two digits) within 09:15 to 15:30. The span is the string; the suggestion is the corrected string where one is obvious. Error.</summary>
    public const string BadTimeLiteral = "strategy.lint.bad_time_literal";

    /// <summary>
    /// A value of the wrong type for its place: an operator or builtin given the wrong type (<c>and</c> of a number, <c>+</c> of a true/false value), a comparison between types that cannot be compared (a number with
    /// <c>time</c>, <c>regime()</c> with a number outside 1 to 9, two <c>regime()</c>), a condition that is not true/false, a text anywhere but next to <c>time</c> or <c>regime()</c>, or a parameter that is not a
    /// finite number. The message names both sides of a comparison. The span is the whole comparison, otherwise the operand. Error.
    /// </summary>
    public const string TypeMismatch = "strategy.lint.type_mismatch";

    /// <summary>A true/false part of the condition that reads no data and is the same on every bar (<c>1 &lt; 2</c>, <c>true</c>, <c>crosses_above(1, 2)</c>, <c>rsi_len &gt; 5</c> with a parameter). The span is the largest such part. Warning.</summary>
    public const string ConstantCondition = "strategy.lint.constant_condition";

    /// <summary><c>tf("15m")</c> on a feature when the strategy already runs on 15m. The span is the whole feature; the suggestion is the feature without <c>tf(…)</c>. Warning.</summary>
    public const string RedundantTimeframe = "strategy.lint.redundant_timeframe";

    /// <summary>The condition reads a feature of the daily market context, so the frame must be created with <see cref="Indicators.FrameOptions.Context"/>. The span is the first such feature. Information, reported only when the condition has no error (a condition that cannot be used needs nothing).</summary>
    public const string NeedsContext = "strategy.lint.needs_context";

    /// <summary>The condition reads <c>is_expiry_day</c>, so the frame must be created with <see cref="Indicators.FrameOptions.ExpiryDays"/>. The span is the first such feature. Information, reported only when the condition has no error.</summary>
    public const string NeedsExpiryDays = "strategy.lint.needs_expiry_days";

    /// <summary>
    /// A strategy parameter with the name of a catalog feature, a builtin, a constant or <c>time</c>: the parameter wins (parameter, then builtin, then feature) and every use in the condition reads the parameter, not
    /// the feature. Not in the spec's table; added for the rule that a parameter that shadows a feature wins "with a warning". The span is the first use. Warning.
    /// </summary>
    public const string ShadowedName = "strategy.lint.shadowed_name";
}
