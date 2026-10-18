namespace XDrishti.Strategies.Tests.Model;

/// <summary>
/// The stored contract of schema 1 (ADR 0002, ADR 0036), written by hand from the spec P3-02b and not generated: the public types of <c>XDrishti.Strategies.Model</c> with their members, the enum <c>Side</c>, the
/// policy-to-parameter tables of the exit policies the compile checks, the filter names and the problem codes. A C# constant, so that no unit test reads a file. Renaming or removing a member, a value, a parameter name
/// or a code, or changing a bound, fails <see cref="ModelSnapshotTests"/>: a stored strategy (or a client that switches on a code) would break.
/// </summary>
internal static class ModelSchemaSnapshot
{
    public const string Text = """
        schema 1
        enum XDrishti.Domain.Research.Side: Buy=0 Sell=1
        enum EntryKind: AtClose=0 StopAboveHigh=1 LimitPullback=2
        enum ExitPolicy: FixedRr=0 PartialBe=1 PartialTrail=2 AtrTrail=3 StructureTrail=4 EmaTrail=5 Pyramid=6
        enum ListMode: All=0 Any=1
        enum OverlayKind: TimeStop=0 EventExit=1
        enum StopKind: FixedPct=0 AtrMult=1 SwingLow=2 SwingHigh=3 BarLow=4 BarHigh=5 OrOpposite=6
        enum Style: Intraday=0 Swing=1
        record ConditionList(ListMode Mode, IReadOnlyList<string> Items)
        record EntrySpec(EntryKind Kind)
        record ExitParameter(string Name, IReadOnlyList<Quantity> Values)
        record ExitSpec(ExitPolicy Policy, IReadOnlyList<ExitParameter> Parameters)
        record OverlaySpec(OverlayKind Kind, IReadOnlyList<ExitParameter> Parameters)
        record Quantity(double? Number, string? Param)
        record SideRules(ConditionList? Setup, ConditionList? Trigger, ConditionList? Invalidate, EntrySpec? Entry, StopSpec? Stop, IReadOnlyList<ExitSpec>? Exits, IReadOnlyList<OverlaySpec>? Overlays)
        record StopSpec(StopKind Kind, Timeframe? Timeframe, Quantity? Pct, Quantity? Mult, Quantity? Lookback, Quantity? Minutes, Quantity? BufferAtr, Quantity? AtrPeriod)
        record StrategyDefinition(int Schema, string Id, Style Style, IReadOnlyList<Timeframe> Timeframes, IReadOnlyList<Side> Sides, IReadOnlyList<StrategyParam> Params, SideRules? Buy, SideRules? Sell, TimeRules? TimeRules, IReadOnlyList<string> Filters)
        const StrategyDefinition.CurrentSchema = 1
        const StrategyDefinition.MaxParams = 4
        const StrategyDefinition.MaxItemsPerList = 100
        record StrategyParam(string Name, double Value)
        record TimeRules(TimeOnly? FirstEntry, TimeOnly? LastEntry, TimeOnly? SquareOff, int? MaxHoldSessions)
        exit FIXED_RR: rr list (0,10]
        exit PARTIAL_TRAIL: book_pct scalar (0,100)
        exit PARTIAL_TRAIL: t1_rr scalar (0,10]
        exit PARTIAL_TRAIL: trail_atr_mult scalar (0,20]
        exit PARTIAL_TRAIL: atr_period scalar optional catalog
        exit values: at most 10 per list
        filters: not_event_day min_turnover
        code strategy.model.bad_id
        code strategy.model.bad_param_name
        code strategy.model.bad_param_value
        code strategy.model.bad_quantity
        code strategy.model.bad_time
        code strategy.model.bad_timeframe
        code strategy.model.duplicate_exit
        code strategy.model.duplicate_filter
        code strategy.model.duplicate_param
        code strategy.model.duplicate_side
        code strategy.model.duplicate_timeframe
        code strategy.model.entry_missing
        code strategy.model.exits_empty
        code strategy.model.feature_invalid
        code strategy.model.field_missing
        code strategy.model.field_not_used
        code strategy.model.field_shape
        code strategy.model.filter_not_evaluated
        code strategy.model.list_empty
        code strategy.model.list_too_long
        code strategy.model.max_hold_on_intraday
        code strategy.model.max_hold_out_of_range
        code strategy.model.param_shadows_name
        code strategy.model.schema_unsupported
        code strategy.model.sell_block_missing
        code strategy.model.side_not_declared
        code strategy.model.side_rules_missing
        code strategy.model.side_rules_unused
        code strategy.model.sides_empty
        code strategy.model.square_off_on_swing
        code strategy.model.stop_missing
        code strategy.model.stop_wrong_side
        code strategy.model.style_timeframe_mismatch
        code strategy.model.swing_sell_unsupported
        code strategy.model.time_rule_missing
        code strategy.model.time_rule_on_daily
        code strategy.model.time_rules_out_of_order
        code strategy.model.timeframe_not_declared
        code strategy.model.timeframes_empty
        code strategy.model.too_many_params
        code strategy.model.too_many_problems
        code strategy.model.trigger_missing
        code strategy.model.undefined_value
        code strategy.model.unknown_filter
        code strategy.model.unknown_param
        code strategy.model.value_out_of_range
        """;
}
