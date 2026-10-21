using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// One failing example for every code of <c>ModelCodes</c> and a near miss that compiles without that problem: the definition that produces <paramref name="Code"/> at <paramref name="Path"/> with
/// <paramref name="Severity"/>, and a definition that differs from it by the one thing that was wrong. The rows are written from the spec (the tables of <i>Contract</i> and the criteria 3 to 12), not driven from the
/// validator's data. A convention test walks the constants of <c>ModelCodes</c> and fails for a code that has no row.
/// </summary>
public sealed record ModelCodeExample(
    string Code,
    string Path,
    ProblemSeverity Severity,
    StrategyDefinition Bad,
    StrategyDefinition Near,
    Timeframe BadTimeframe = Timeframe.M15,
    Side BadSide = Side.Buy,
    Timeframe? NearTimeframe = null,
    Side? NearSide = null)
{
    public override string ToString() => Code + " at " + Path;

    public static IReadOnlyList<ModelCodeExample> All { get; } = Build();

    private static StrategyDefinition Basic() => Defs.Basic();

    private static ModelCodeExample Row(string name, string path, StrategyDefinition bad, StrategyDefinition near, ProblemSeverity severity = ProblemSeverity.Error, Timeframe timeframe = Timeframe.M15, Side side = Side.Buy, Timeframe? nearTimeframe = null, Side? nearSide = null) =>
        new("strategy.model." + name, path, severity, bad, near, timeframe, side, nearTimeframe, nearSide);

    private static StrategyDefinition WithStop(StopSpec stop) => Basic().WithBuy(b => b with { Stop = stop });

    private static ExitSpec AtrTrail => new(ExitPolicy.AtrTrail, [new ExitParameter("mult", [Quantity.Of(2)]), new ExitParameter("activate_at_rr", [Quantity.Of(1)])]);

    private static StrategyDefinition WithExits(params ExitSpec[] exits) => Basic().WithBuy(b => b with { Exits = exits });

    private static List<ModelCodeExample> Build()
    {
        var swingLow = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5));
        var a = new StrategyParam("aa", 1);
        var b = new StrategyParam("bb", 2);
        var rows = new List<ModelCodeExample>
        {
            // definition
            Row("schema_unsupported", "schema", Basic() with { Schema = 2 }, Basic()),
            Row("bad_id", "id", Basic() with { Id = "1x" }, Basic() with { Id = "A1_b" }),
            Row("undefined_value", "style", Basic() with { Style = (Style)7 }, Basic()),
            Row("timeframes_empty", "timeframes", Basic() with { Timeframes = [] }, Basic()),
            Row("bad_timeframe", "timeframes[1]", Basic() with { Timeframes = [Timeframe.M15, Timeframe.M1] }, Basic() with { Timeframes = [Timeframe.M15, Timeframe.M5] }),
            Row("duplicate_timeframe", "timeframes[1]", Basic() with { Timeframes = [Timeframe.M15, Timeframe.M15] }, Basic() with { Timeframes = [Timeframe.M15, Timeframe.M5] }),
            Row("timeframe_not_declared", "timeframes", Basic() with { Timeframes = [Timeframe.M5] }, Basic() with { Timeframes = [Timeframe.M5] }, nearTimeframe: Timeframe.M5),
            Row("style_timeframe_mismatch", "timeframes[1]", Basic() with { Timeframes = [Timeframe.M15, Timeframe.D1] }, Basic() with { Timeframes = [Timeframe.M15, Timeframe.H1] }),
            Row("sides_empty", "sides", Basic() with { Sides = [] }, Basic()),
            Row("duplicate_side", "sides[1]", Basic() with { Sides = [Side.Buy, Side.Buy] }, Basic()),
            Row("side_not_declared", "sides", Basic() with { Sides = [Side.Sell] }, Basic()),
            Row("side_rules_missing", "buy", Basic() with { Buy = null }, Basic()),
            Row("sell_block_missing", "sell", Basic() with { Sell = null }, Basic(), side: Side.Sell),
            Row("swing_sell_unsupported", "sides[1]", Defs.Swing() with { Sides = [Side.Buy, Side.Sell] }, Defs.Swing(), timeframe: Timeframe.H1),
            Row("side_rules_unused", "sell", Basic() with { Sides = [Side.Buy] }, Basic(), ProblemSeverity.Warning),

            // parameters
            Row("too_many_params", "params", Basic() with { Params = [.. Enumerable.Range(0, 5).Select(i => new StrategyParam("p" + (char)('a' + i), i))] }, Basic() with { Params = [.. Enumerable.Range(0, 4).Select(i => new StrategyParam("p" + (char)('a' + i), i))] }),
            Row("duplicate_param", "params[1]", Basic() with { Params = [a, a] }, Basic() with { Params = [a, b] }),
            Row("bad_param_name", "params[0]", Basic() with { Params = [new StrategyParam("RSI_LEN", 1)] }, Basic() with { Params = [new StrategyParam("rsi_len", 1)] }),
            Row("bad_param_value", "params[0]", Basic() with { Params = [new StrategyParam("aa", double.NaN)] }, Basic() with { Params = [a] }),
            Row("param_shadows_name", "params[0]", Basic() with { Params = [new StrategyParam("close", 1)] }, Basic() with { Params = [new StrategyParam("my_close", 1)] }, ProblemSeverity.Warning),
            Row("unknown_param", "buy.stop.lookback", Basic().WithBuy(r => r with { Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("zzz")) }) with { Params = [a] }, Basic().WithBuy(r => r with { Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("aa")) }) with { Params = [a] }),
            Row("bad_quantity", "buy.stop.pct", WithStop(new StopSpec(StopKind.FixedPct, Pct: new Quantity(null, null))), WithStop(Defs.FixedPct())),

            // sections
            Row("trigger_missing", "buy.trigger", Basic().WithBuy(r => r with { Trigger = null }), Basic()),
            Row("list_empty", "buy.trigger", Basic().WithBuy(r => r with { Trigger = Defs.All() }), Basic()),
            Row("list_too_long", "buy.trigger", Basic().WithBuy(r => r with { Trigger = Defs.All([.. Enumerable.Repeat("close > open", 101)]) }), Basic().WithBuy(r => r with { Trigger = Defs.All([.. Enumerable.Repeat("close > open", 100)]) })),
            Row("entry_missing", "buy.entry", Basic().WithBuy(r => r with { Entry = null }), Basic()),
            Row("stop_missing", "buy.stop", Basic().WithBuy(r => r with { Stop = null }), Basic()),
            Row("exits_empty", "buy.exits", Basic().WithBuy(r => r with { Exits = [] }), Basic()),

            // stop and exits
            Row("stop_wrong_side", "buy.stop", WithStop(new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Of(5))), WithStop(swingLow)),
            Row("field_missing", "buy.stop.lookback", WithStop(new StopSpec(StopKind.SwingLow)), WithStop(swingLow)),
            Row("field_not_used", "buy.stop.pct", WithStop(new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), Pct: Quantity.Of(1))), WithStop(new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2)))),
            Row("field_shape", "buy.exits[0].rr", WithExits(new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [])])), WithExits(Defs.FixedRr(2))),
            Row("value_out_of_range", "buy.stop.pct", WithStop(Defs.FixedPct(51)), WithStop(Defs.FixedPct(50))),
            Row("feature_invalid", "buy.stop.lookback", WithStop(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(0))), WithStop(new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(1)))),
            Row("duplicate_exit", "buy.exits[1]", WithExits(Defs.FixedRr(2), Defs.FixedRr(3)), WithExits(Defs.FixedRr(2), Defs.PartialTrail())),

            Row("exit_not_evaluated", "buy.exits[1]", WithExits(Defs.FixedRr(2), AtrTrail), WithExits(Defs.FixedRr(2)), ProblemSeverity.Warning),

            // time rules and filters
            Row("time_rule_missing", "time_rules.square_off", Basic() with { TimeRules = Defs.IntradayRules with { SquareOff = null } }, Basic()),
            Row("bad_time", "time_rules.first_entry", Basic() with { TimeRules = Defs.IntradayRules with { FirstEntry = new TimeOnly(9, 14) } }, Basic() with { TimeRules = Defs.IntradayRules with { FirstEntry = new TimeOnly(9, 15) } }),
            Row("time_rules_out_of_order", "time_rules.last_entry", Basic() with { TimeRules = Defs.IntradayRules with { FirstEntry = new TimeOnly(11, 30), LastEntry = new TimeOnly(9, 30) } }, Basic() with { TimeRules = Defs.IntradayRules with { FirstEntry = new TimeOnly(11, 30), LastEntry = new TimeOnly(11, 30) } }),
            Row("time_rule_on_daily", "time_rules.first_entry", Defs.Swing() with { TimeRules = new TimeRules(new TimeOnly(9, 30), null, null, 5) }, Defs.Swing(), timeframe: Timeframe.D1),
            Row("square_off_on_swing", "time_rules.square_off", Defs.Swing() with { TimeRules = new TimeRules(null, null, new TimeOnly(15, 15), 5) }, Defs.Swing(), timeframe: Timeframe.H1),
            Row("max_hold_on_intraday", "time_rules.max_hold_sessions", Basic() with { TimeRules = Defs.IntradayRules with { MaxHoldSessions = 1 } }, Basic()),
            Row("max_hold_out_of_range", "time_rules.max_hold_sessions", Defs.Swing() with { TimeRules = new TimeRules(null, null, null, 6) }, Defs.Swing() with { TimeRules = new TimeRules(null, null, null, 5) }, timeframe: Timeframe.H1),
            Row("unknown_filter", "filters[0]", Basic() with { Filters = ["not_event_dy"] }, Basic() with { Filters = [] }),
            Row("filter_not_evaluated", "filters[0]", Basic() with { Filters = [FilterNames.NotEventDay] }, Basic() with { Filters = [] }, ProblemSeverity.Warning),
            Row("duplicate_filter", "filters[1]", Basic() with { Filters = [FilterNames.NotEventDay, FilterNames.NotEventDay] }, Basic() with { Filters = [FilterNames.NotEventDay, FilterNames.MinTurnover] }),

            // the cap
            Row("too_many_problems", string.Empty, Basic().WithBuy(r => r with { Trigger = Defs.All([.. Enumerable.Repeat("closee > 1", 101)]) }), Basic().WithBuy(r => r with { Trigger = Defs.All([.. Enumerable.Repeat("closee > 1", 100)]) })),
        };
        return rows;
    }
}
