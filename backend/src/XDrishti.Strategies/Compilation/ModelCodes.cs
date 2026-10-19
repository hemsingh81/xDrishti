namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The stable codes of the problems of the strategy model and its compile (<c>strategy.model.*</c>, spec P3-02b): 47 codes in six groups. The problems of the conditions inside a strategy keep the codes of the language
/// (<c>strategy.syntax.*</c>, <c>strategy.lint.*</c>) and add a path. A program switches on the code and the path; the messages are English and may change. A model problem has no text, so its span is (0, 0, line 1, column 1).
/// The names are a stored contract (ADR 0002): a snapshot test fails for a renamed or removed code.
/// </summary>
public static class ModelCodes
{
    private const string Prefix = "strategy.model.";

    // ---- Definition --------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The <c>Schema</c> is not the one this library reads (<c>StrategyDefinition.CurrentSchema</c>). Path <c>schema</c>. Error.</summary>
    public const string SchemaUnsupported = Prefix + "schema_unsupported";

    /// <summary>The id is empty, does not match <c>[A-Za-z][A-Za-z0-9_]*</c> or is longer than 64 characters. Path <c>id</c>. Error.</summary>
    public const string BadId = Prefix + "bad_id";

    /// <summary>An enum field holds a number that is no member of the closed set (<c>(Style)7</c>). Path: that field (<c>style</c>, <c>buy.stop.kind</c>, <c>buy.trigger.mode</c>, <c>sides[1]</c>). Error.</summary>
    public const string UndefinedValue = Prefix + "undefined_value";

    /// <summary>The strategy lists no timeframe. Path <c>timeframes</c>. Error.</summary>
    public const string TimeframesEmpty = Prefix + "timeframes_empty";

    /// <summary>A timeframe that is not an entry timeframe (5m 10m 15m 30m 1h 1D). Path <c>timeframes[i]</c>. Error.</summary>
    public const string BadTimeframe = Prefix + "bad_timeframe";

    /// <summary>A timeframe listed twice; reported at the second. Path <c>timeframes[i]</c>. Error.</summary>
    public const string DuplicateTimeframe = Prefix + "duplicate_timeframe";

    /// <summary>The evaluation timeframe asked for is not in the strategy's <c>timeframes</c>. Path <c>timeframes</c>. Error.</summary>
    public const string TimeframeNotDeclared = Prefix + "timeframe_not_declared";

    /// <summary>An intraday strategy lists 1D, or a swing strategy lists 5m, 10m or 15m (doc 07 §3 and §4; the author's proposal, ADR 0036). Path <c>timeframes[i]</c>. Error.</summary>
    public const string StyleTimeframeMismatch = Prefix + "style_timeframe_mismatch";

    /// <summary>The strategy lists no side. Path <c>sides</c>. Error.</summary>
    public const string SidesEmpty = Prefix + "sides_empty";

    /// <summary>A side listed twice; reported at the second. Path <c>sides[i]</c>. Error.</summary>
    public const string DuplicateSide = Prefix + "duplicate_side";

    /// <summary>The side compiled is not in the strategy's <c>sides</c>. Path <c>sides</c>. Error.</summary>
    public const string SideNotDeclared = Prefix + "side_not_declared";

    /// <summary>The BUY side is compiled and the strategy has no <c>buy</c> rules. Reported only by the compile of that side. Path <c>buy</c>. Error.</summary>
    public const string SideRulesMissing = Prefix + "side_rules_missing";

    /// <summary>The SELL side is compiled and the strategy has no <c>sell</c> block (the automatic mirror of the BUY block is P3-02e). Reported only by the compile of that side. Path <c>sell</c>. Error.</summary>
    public const string SellBlockMissing = Prefix + "sell_block_missing";

    /// <summary>A swing strategy lists SELL: swing SELL is futures only and there are no futures in P3 (doc 07 §7). Path <c>sides[i]</c>. Error.</summary>
    public const string SwingSellUnsupported = Prefix + "swing_sell_unsupported";

    /// <summary>A side has rules but is not in <c>sides</c>. Path <c>buy</c> or <c>sell</c>. Warning.</summary>
    public const string SideRulesUnused = Prefix + "side_rules_unused";

    // ---- Parameters and quantities -----------------------------------------------------------------------------------------------------------------------------

    /// <summary>More than <c>StrategyDefinition.MaxParams</c> parameters; one problem however many are over. Path <c>params</c>. Error.</summary>
    public const string TooManyParams = Prefix + "too_many_params";

    /// <summary>A parameter name used twice; reported at the second. Path <c>params[i]</c>. Error.</summary>
    public const string DuplicateParam = Prefix + "duplicate_param";

    /// <summary>A parameter name that does not match <c>[a-z][a-z0-9_]*</c> or is longer than 32 characters. Path <c>params[i]</c>. Error.</summary>
    public const string BadParamName = Prefix + "bad_param_name";

    /// <summary>A parameter value that is NaN or infinite. Path <c>params[i]</c>. Error.</summary>
    public const string BadParamValue = Prefix + "bad_param_value";

    /// <summary>A parameter named like a catalog feature, a builtin or <c>time</c>: the parameter wins in every condition. Path <c>params[i]</c>. Warning.</summary>
    public const string ParamShadowsName = Prefix + "param_shadows_name";

    /// <summary>A quantity names a parameter that is not declared; the suggestion is the closest declared name. Path: the field. Error.</summary>
    public const string UnknownParam = Prefix + "unknown_param";

    /// <summary>A quantity with neither or both of its number and its parameter name, or a literal that is not finite. Path: the field. Error.</summary>
    public const string BadQuantity = Prefix + "bad_quantity";

    // ---- Sections ----------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The side has no <c>trigger</c> list. Path <c>buy.trigger</c>. Error.</summary>
    public const string TriggerMissing = Prefix + "trigger_missing";

    /// <summary>A list that is present has no items. Path: the list. Error.</summary>
    public const string ListEmpty = Prefix + "list_empty";

    /// <summary>A list has more than <c>StrategyDefinition.MaxItemsPerList</c> items; only the first are compiled. Path: the list. Error.</summary>
    public const string ListTooLong = Prefix + "list_too_long";

    /// <summary>The side has no <c>entry</c>. Path <c>buy.entry</c>. Error.</summary>
    public const string EntryMissing = Prefix + "entry_missing";

    /// <summary>The side has no <c>stop</c>. Path <c>buy.stop</c>. Error.</summary>
    public const string StopMissing = Prefix + "stop_missing";

    /// <summary>The side has no exit policy. Path <c>buy.exits</c>. Error.</summary>
    public const string ExitsEmpty = Prefix + "exits_empty";

    // ---- Stop and exits ----------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A stop kind of the other side (<c>swing_high</c> or <c>bar_high</c> on BUY, <c>swing_low</c> or <c>bar_low</c> on SELL). Path <c>buy.stop</c>. Error.</summary>
    public const string StopWrongSide = Prefix + "stop_wrong_side";

    /// <summary>A field the stop kind or the policy requires is not given. Path: the field. Error.</summary>
    public const string FieldMissing = Prefix + "field_missing";

    /// <summary>A field the stop kind or the policy does not use is given. Path: the field. Error.</summary>
    public const string FieldNotUsed = Prefix + "field_not_used";

    /// <summary>The shape of a field is wrong: a list where a scalar is wanted, the wrong number of values, a name given twice, a bad parameter name. Path: the field. Error.</summary>
    public const string FieldShape = Prefix + "field_shape";

    /// <summary>A number outside the sanity bounds of its field. Path: the field (<c>buy.exits[0].rr[1]</c> for a value of a list). Error.</summary>
    public const string ValueOutOfRange = Prefix + "value_out_of_range";

    /// <summary>The feature a stop or an exit reads is refused by the indicator catalog (a period out of range, not a whole number, an opening range that is not whole bars, a timeframe the feature cannot run on); the message is the catalog's. Path: the field. Error.</summary>
    public const string FeatureInvalid = Prefix + "feature_invalid";

    /// <summary>A candidate policy, or an overlay, listed twice; reported at the second. Path <c>buy.exits[i]</c> or <c>buy.overlays[i]</c>. Error.</summary>
    public const string DuplicateExit = Prefix + "duplicate_exit";

    /// <summary>A candidate policy other than <c>FIXED_RR</c>, or an overlay, that no simulator runs yet (P4; for <c>event_exit</c> also the results calendar, P15): the table is checked and the strategy compiles, but a simulator refuses it. One per policy or overlay. Path <c>buy.exits[i]</c> or <c>buy.overlays[i]</c>. Warning.</summary>
    public const string ExitNotEvaluated = Prefix + "exit_not_evaluated";

    // ---- Time rules and filters --------------------------------------------------------------------------------------------------------------------------------

    /// <summary>An intraday strategy without <c>square_off</c>, or a swing strategy without <c>max_hold_sessions</c> (SESSION_EXIT and MAX_HOLD are always on, doc 08 §2). Path <c>time_rules.square_off</c> or <c>time_rules.max_hold_sessions</c>. Error.</summary>
    public const string TimeRuleMissing = Prefix + "time_rule_missing";

    /// <summary>A time before 09:15, after 15:30 or with seconds. Path: the field. Error.</summary>
    public const string BadTime = Prefix + "bad_time";

    /// <summary>The order <c>first_entry</c> &lt;= <c>last_entry</c> &lt; <c>square_off</c> is broken; reported at the later field. Error.</summary>
    public const string TimeRulesOutOfOrder = Prefix + "time_rules_out_of_order";

    /// <summary><c>first_entry</c> or <c>last_entry</c> on a 1D evaluation timeframe, where a bar has no time of day. Path: the field. Error.</summary>
    public const string TimeRuleOnDaily = Prefix + "time_rule_on_daily";

    /// <summary><c>square_off</c> on a swing strategy. Path <c>time_rules.square_off</c>. Error.</summary>
    public const string SquareOffOnSwing = Prefix + "square_off_on_swing";

    /// <summary><c>max_hold_sessions</c> on an intraday strategy. Path <c>time_rules.max_hold_sessions</c>. Error.</summary>
    public const string MaxHoldOnIntraday = Prefix + "max_hold_on_intraday";

    /// <summary><c>max_hold_sessions</c> outside 1 to 5. Path <c>time_rules.max_hold_sessions</c>. Error.</summary>
    public const string MaxHoldOutOfRange = Prefix + "max_hold_out_of_range";

    /// <summary>A filter name that is not <c>not_event_day</c> or <c>min_turnover</c>; the suggestion is the closest. Path <c>filters[i]</c>. Error.</summary>
    public const string UnknownFilter = Prefix + "unknown_filter";

    /// <summary>A known filter that nothing in P3 evaluates (no results calendar, no liquidity data). Path <c>filters[i]</c>. Warning.</summary>
    public const string FilterNotEvaluated = Prefix + "filter_not_evaluated";

    /// <summary>A filter listed twice; reported at the second. Path <c>filters[i]</c>. Error.</summary>
    public const string DuplicateFilter = Prefix + "duplicate_filter";

    // ---- Cap ---------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>More problems than are listed: the first 100 errors, or the first 100 warnings and notes, are returned and this one stands in place of the rest. An error when errors were dropped, an information when only warnings were. Path empty. Span (0, 0, 1, 1).</summary>
    public const string TooManyProblems = Prefix + "too_many_problems";

}
