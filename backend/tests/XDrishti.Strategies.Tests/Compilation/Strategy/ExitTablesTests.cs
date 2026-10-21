using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Spec P3-02b2, criteria 1 to 10 and 18: the tables of <c>PARTIAL_BE</c>, <c>ATR_TRAIL</c>, <c>STRUCTURE_TRAIL</c>, <c>EMA_TRAIL</c>, <c>PYRAMID</c> and of the overlays <c>time_stop</c> and <c>event_exit</c>. The rows are
/// literals written from the tables of the spec, not driven from <c>ExitTables</c>, so that a renamed parameter or a changed bound fails here. Every parameter has a failing row (exact code and path) and a near miss
/// (an empty list of errors).
/// </summary>
public sealed class ExitTablesTests
{
    private const string Missing = "model.field_missing@";

    private const string NotUsed = "model.field_not_used@";

    private const string Shape = "model.field_shape@";

    private const string Range = "model.value_out_of_range@";

    private const string Invalid = "model.feature_invalid@";

    private const string X = "buy.exits[0].";

    private const string O = "buy.overlays[0].";

    private static ExitParameter P(string name, params double[] values) => Defs.P(name, values);

    private static ExitParameter[] Set(ExitParameter[] baseline, params ExitParameter[] changes) =>
        [.. baseline.Where(p => changes.All(c => c.Name != p.Name)), .. changes];

    private static ExitParameter[] Drop(ExitParameter[] baseline, string name) => [.. baseline.Where(p => p.Name != name)];

    private static ExitParameter[] BeBase => [P("book_pct", 50), P("t1_rr", 1), P("t2_rr", 2, 3)];

    private static ExitParameter[] AtrBase => [P("mult", 2, 3), P("activate_at_rr", 1)];

    private static ExitParameter[] StructBase => [P("lookback_bars", 3)];

    private static ExitParameter[] EmaBase => [P("length", 20)];

    private static ExitParameter[] PyramidBase => [P("initial_pct", 50), P("add_at_rr", 1, 2), P("add_pct", 30, 20), P("max_adds", 2), P("trail_atr_mult", 2)];

    private static ExitParameter[] TimeStopBase => [P("min_progress_rr", 0.5), P("intraday_bars", 6)];

    private static StrategyDefinition Intraday(Timeframe timeframe = Timeframe.M15) => Defs.Basic() with { Timeframes = [timeframe], Params = [new StrategyParam("aa", 3)] };

    private static StrategyDefinition Exit(ExitPolicy policy, ExitParameter[] parameters, Timeframe timeframe = Timeframe.M15) =>
        Intraday(timeframe).WithBuy(b => b with { Exits = [new ExitSpec(policy, parameters)] });

    private static StrategyDefinition Overlay(OverlayKind kind, ExitParameter[] parameters, Timeframe timeframe = Timeframe.M15) =>
        Intraday(timeframe).WithBuy(b => b with { Overlays = [new OverlaySpec(kind, parameters)] });

    private static StrategyDefinition SwingStop(ExitParameter[] parameters, int? maxHold, Timeframe timeframe = Timeframe.H1) =>
        (Defs.Swing() with { Timeframes = [timeframe], TimeRules = new TimeRules(null, null, null, maxHold) }).WithBuy(b => b with { Overlays = [new OverlaySpec(OverlayKind.TimeStop, parameters)] });

    // ---- the rows ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, StrategyDefinition, Timeframe, string[]> Rows { get; } = Build();

    private static TheoryData<string, StrategyDefinition, Timeframe, string[]> Build()
    {
        var rows = new List<(string Name, StrategyDefinition Definition, Timeframe Timeframe, string[] Errors)>();

        void Add(string name, StrategyDefinition definition, params string[] errors) => rows.Add((name, definition, Timeframe.M15, errors));

        void AddOn(string name, StrategyDefinition definition, Timeframe timeframe, params string[] errors) => rows.Add((name, definition, timeframe, errors));

        void Be(string name, ExitParameter[] parameters, params string[] errors) => Add("PARTIAL_BE " + name, Exit(ExitPolicy.PartialBe, parameters), errors);

        void Atr(string name, ExitParameter[] parameters, params string[] errors) => Add("ATR_TRAIL " + name, Exit(ExitPolicy.AtrTrail, parameters), errors);

        void Structure(string name, ExitParameter[] parameters, params string[] errors) => Add("STRUCTURE_TRAIL " + name, Exit(ExitPolicy.StructureTrail, parameters), errors);

        void Ema(string name, ExitParameter[] parameters, params string[] errors) => Add("EMA_TRAIL " + name, Exit(ExitPolicy.EmaTrail, parameters), errors);

        void Pyramid(string name, ExitParameter[] parameters, params string[] errors) => Add("PYRAMID " + name, Exit(ExitPolicy.Pyramid, parameters), errors);

        void Stop(string name, ExitParameter[] parameters, params string[] errors) => Add("time_stop " + name, Overlay(OverlayKind.TimeStop, parameters), errors);

        void Event(string name, ExitParameter[] parameters, params string[] errors) => Add("event_exit " + name, Overlay(OverlayKind.EventExit, parameters), errors);

        // PARTIAL_BE
        Be("valid", BeBase);
        Be("book_pct missing", Drop(BeBase, "book_pct"), Missing + X + "book_pct");
        Be("t1_rr missing", Drop(BeBase, "t1_rr"), Missing + X + "t1_rr");
        Be("t2_rr missing", Drop(BeBase, "t2_rr"), Missing + X + "t2_rr");
        Be("book_pct two values", Set(BeBase, P("book_pct", 50, 60)), Shape + X + "book_pct");
        Be("book_pct 0", Set(BeBase, P("book_pct", 0)), Range + X + "book_pct");
        Be("book_pct 100", Set(BeBase, P("book_pct", 100)), Range + X + "book_pct");
        Be("book_pct 101", Set(BeBase, P("book_pct", 101)), Range + X + "book_pct");
        Be("book_pct 99.9", Set(BeBase, P("book_pct", 99.9)));
        Be("book_pct 0.1", Set(BeBase, P("book_pct", 0.1)));
        Be("t1_rr two values", Set(BeBase, P("t1_rr", 1, 1.5)), Shape + X + "t1_rr");
        Be("t1_rr 0 is one error, the relation is not reported", Set(BeBase, P("t1_rr", 0)), Range + X + "t1_rr");
        Be("t1_rr 10.1 is one error, the relation is not reported", Set(BeBase, P("t1_rr", 10.1)), Range + X + "t1_rr");
        Be("t1_rr 10 is in range, so only the relation fails", Set(BeBase, P("t1_rr", 10)), Range + X + "t2_rr[0]", Range + X + "t2_rr[1]");
        Be("t1_rr 9.9 with t2_rr 10", Set(BeBase, P("t1_rr", 9.9), P("t2_rr", 10)));
        Be("t2_rr 0", Set(BeBase, P("t2_rr", 0)), Range + X + "t2_rr[0]");
        Be("t2_rr second value 10.1", Set(BeBase, P("t2_rr", 2, 10.1)), Range + X + "t2_rr[1]");
        Be("t2_rr 10", Set(BeBase, P("t2_rr", 10)));
        Be("t2_rr 11 values", Set(BeBase, P("t2_rr", 2, 3, 4, 5, 6, 7, 8, 9, 9.5, 9.6, 9.7)), Shape + X + "t2_rr");
        Be("t2_rr of one value", Set(BeBase, P("t2_rr", 2)));
        Be("t2_rr equal to t1_rr", Set(BeBase, P("t1_rr", 2), P("t2_rr", 2)), Range + X + "t2_rr[0]");
        Be("t2_rr just above t1_rr", Set(BeBase, P("t1_rr", 2), P("t2_rr", 2.01)));
        Be("t2_rr unordered above t1_rr", Set(BeBase, P("t2_rr", 3, 2)));
        Be("t2_rr second below t1_rr", Set(BeBase, P("t2_rr", 3, 0.9)), Range + X + "t2_rr[1]");
        Be("t2_rr given twice", [.. BeBase, P("t2_rr", 4)], Shape + X + "t2_rr");
        Be("an unknown name", [.. BeBase, P("trail", 2)], NotUsed + X + "trail");
        Be("every required field missing", [], Missing + X + "book_pct", Missing + X + "t1_rr", Missing + X + "t2_rr");

        // ATR_TRAIL
        Atr("valid", AtrBase);
        Atr("mult missing", Drop(AtrBase, "mult"), Missing + X + "mult");
        Atr("activate_at_rr missing", Drop(AtrBase, "activate_at_rr"), Missing + X + "activate_at_rr");
        Atr("mult of one value", Set(AtrBase, P("mult", 2)));
        Atr("mult of ten values", Set(AtrBase, P("mult", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)));
        Atr("mult 11 values", Set(AtrBase, P("mult", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)), Shape + X + "mult");
        Atr("mult none", Set(AtrBase, new ExitParameter("mult", [])), Shape + X + "mult");
        Atr("mult 0", Set(AtrBase, P("mult", 0)), Range + X + "mult[0]");
        Atr("mult second value 20.1", Set(AtrBase, P("mult", 2, 20.1)), Range + X + "mult[1]");
        Atr("mult 20", Set(AtrBase, P("mult", 20)));
        Atr("mult 0.1", Set(AtrBase, P("mult", 0.1)));
        Atr("activate_at_rr two values", Set(AtrBase, P("activate_at_rr", 1, 2)), Shape + X + "activate_at_rr");
        Atr("activate_at_rr -0.1", Set(AtrBase, P("activate_at_rr", -0.1)), Range + X + "activate_at_rr");
        Atr("activate_at_rr 10.1", Set(AtrBase, P("activate_at_rr", 10.1)), Range + X + "activate_at_rr");
        Atr("activate_at_rr 0", Set(AtrBase, P("activate_at_rr", 0)));
        Atr("activate_at_rr 10", Set(AtrBase, P("activate_at_rr", 10)));
        Atr("atr_period 14.5", [.. AtrBase, P("atr_period", 14.5)], Invalid + X + "atr_period");
        Atr("atr_period 0", [.. AtrBase, P("atr_period", 0)], Invalid + X + "atr_period");
        Atr("atr_period two values", [.. AtrBase, P("atr_period", 10, 20)], Shape + X + "atr_period");
        Atr("atr_period 21", [.. AtrBase, P("atr_period", 21)]);
        Atr("the provisional name activate_at", [.. Drop(AtrBase, "activate_at_rr"), P("activate_at", 1)], NotUsed + X + "activate_at", Missing + X + "activate_at_rr");
        Atr("the provisional name atr_mult", [.. Drop(AtrBase, "mult"), P("atr_mult", 2)], NotUsed + X + "atr_mult", Missing + X + "mult");

        // STRUCTURE_TRAIL and EMA_TRAIL
        Structure("valid", StructBase);
        Structure("lookback_bars missing", [], Missing + X + "lookback_bars");
        Structure("lookback_bars two values", [P("lookback_bars", 3, 4)], Shape + X + "lookback_bars");
        Structure("lookback_bars 3.5", [P("lookback_bars", 3.5)], Invalid + X + "lookback_bars");
        Structure("lookback_bars 0", [P("lookback_bars", 0)], Invalid + X + "lookback_bars");
        Structure("lookback_bars 10001", [P("lookback_bars", 10_001)], Invalid + X + "lookback_bars");
        Structure("lookback_bars 1", [P("lookback_bars", 1)]);
        Structure("lookback_bars 10000", [P("lookback_bars", 10_000)]);
        Structure("the provisional name lookback", [P("lookback", 3)], NotUsed + X + "lookback", Missing + X + "lookback_bars");
        Ema("valid", EmaBase);
        Ema("length missing", [], Missing + X + "length");
        Ema("length two values", [P("length", 20, 30)], Shape + X + "length");
        Ema("length 20.5", [P("length", 20.5)], Invalid + X + "length");
        Ema("length 0", [P("length", 0)], Invalid + X + "length");
        Ema("length 10001", [P("length", 10_001)], Invalid + X + "length");
        Ema("length 1", [P("length", 1)]);
        Ema("length 10000", [P("length", 10_000)]);
        Ema("the provisional name ema_len", [P("ema_len", 20)], NotUsed + X + "ema_len", Missing + X + "length");

        // PYRAMID
        Pyramid("valid", PyramidBase);
        foreach (var name in new[] { "initial_pct", "add_at_rr", "add_pct", "max_adds", "trail_atr_mult" })
        {
            Pyramid(name + " missing", Drop(PyramidBase, name), Missing + X + name);
        }

        Pyramid("initial_pct 0", Set(PyramidBase, P("initial_pct", 0)), Range + X + "initial_pct");
        Pyramid("initial_pct 100.1", Set(PyramidBase, P("initial_pct", 100.1)), Range + X + "initial_pct");
        Pyramid("initial_pct 100 is in range, so only the sum fails", Set(PyramidBase, P("initial_pct", 100)), Range + X + "add_pct");
        Pyramid("initial_pct 100 with no room needed is the sum of nothing", Set(PyramidBase, P("initial_pct", 49.9)));
        Pyramid("initial_pct two values", Set(PyramidBase, P("initial_pct", 50, 40)), Shape + X + "initial_pct");
        Pyramid("add_at_rr 0", Set(PyramidBase, P("add_at_rr", 0, 2)), Range + X + "add_at_rr[0]");
        Pyramid("add_at_rr second value 10.1", Set(PyramidBase, P("add_at_rr", 1, 10.1)), Range + X + "add_at_rr[1]");
        Pyramid("add_at_rr 10", Set(PyramidBase, P("add_at_rr", 1, 10)));
        Pyramid("add_at_rr [1, 1]", Set(PyramidBase, P("add_at_rr", 1, 1)), Range + X + "add_at_rr[1]");
        Pyramid("add_at_rr [2, 1]", Set(PyramidBase, P("add_at_rr", 2, 1)), Range + X + "add_at_rr[1]");
        Pyramid("add_at_rr [1, 1.01]", Set(PyramidBase, P("add_at_rr", 1, 1.01)));
        Pyramid("add_at_rr of one level", [P("initial_pct", 50), P("add_at_rr", 1), P("add_pct", 30), P("max_adds", 1), P("trail_atr_mult", 2)]);
        Pyramid("add_at_rr 11 values", Set(PyramidBase, P("add_at_rr", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10.5)), Shape + X + "add_at_rr");
        Pyramid("add_pct 0", Set(PyramidBase, P("add_pct", 0, 20)), Range + X + "add_pct[0]");
        Pyramid("add_pct second value 100.1", Set(PyramidBase, P("add_pct", 30, 100.1)), Range + X + "add_pct[1]");
        Pyramid("add_pct of one value against two levels", Set(PyramidBase, P("add_pct", 30)), Shape + X + "add_pct");
        Pyramid("add_pct of three values against two levels", Set(PyramidBase, P("add_pct", 10, 10, 10)), Shape + X + "add_pct");
        Pyramid("the sum is exactly 100", Set(PyramidBase, P("add_pct", 30, 20)));
        Pyramid("the sum is 101", Set(PyramidBase, P("add_pct", 30, 21)), Range + X + "add_pct");
        Pyramid("max_adds 3 with two levels", Set(PyramidBase, P("max_adds", 3)), Range + X + "max_adds");
        Pyramid("max_adds 1", Set(PyramidBase, P("max_adds", 1)));
        Pyramid("max_adds 2", Set(PyramidBase, P("max_adds", 2)));
        Pyramid("max_adds 0", Set(PyramidBase, P("max_adds", 0)), Range + X + "max_adds");
        Pyramid("max_adds 11", Set(PyramidBase, P("max_adds", 11)), Range + X + "max_adds");
        Pyramid("max_adds 1.5", Set(PyramidBase, P("max_adds", 1.5)), Shape + X + "max_adds");
        Pyramid("max_adds two values", Set(PyramidBase, P("max_adds", 1, 2)), Shape + X + "max_adds");
        Pyramid("trail_atr_mult 0", Set(PyramidBase, P("trail_atr_mult", 0)), Range + X + "trail_atr_mult");
        Pyramid("trail_atr_mult 20.1", Set(PyramidBase, P("trail_atr_mult", 20.1)), Range + X + "trail_atr_mult");
        Pyramid("trail_atr_mult 20", Set(PyramidBase, P("trail_atr_mult", 20)));
        Pyramid("atr_period 14.5", [.. PyramidBase, P("atr_period", 14.5)], Invalid + X + "atr_period");
        Pyramid("atr_period 10", [.. PyramidBase, P("atr_period", 10)]);
        Pyramid("the dropped field trail", [.. PyramidBase, P("trail", 1)], NotUsed + X + "trail");
        Pyramid("the dropped field cap_total_risk_to_initial", [.. PyramidBase, P("cap_total_risk_to_initial", 1)], NotUsed + X + "cap_total_risk_to_initial");

        // time_stop of an intraday strategy, E = 15m
        Stop("valid", TimeStopBase);
        Stop("min_progress_rr missing", Drop(TimeStopBase, "min_progress_rr"), Missing + O + "min_progress_rr");
        Stop("intraday_bars missing", Drop(TimeStopBase, "intraday_bars"), Missing + O + "intraday_bars");
        Stop("nothing given", [], Missing + O + "min_progress_rr", Missing + O + "intraday_bars");
        Stop("swing_sessions on an intraday strategy", [P("min_progress_rr", 0.5), P("swing_sessions", 2)], NotUsed + O + "swing_sessions", Missing + O + "intraday_bars");
        Stop("both given on an intraday strategy", [.. TimeStopBase, P("swing_sessions", 2)], NotUsed + O + "swing_sessions");
        Stop("min_progress_rr -0.1", Set(TimeStopBase, P("min_progress_rr", -0.1)), Range + O + "min_progress_rr");
        Stop("min_progress_rr 0", Set(TimeStopBase, P("min_progress_rr", 0)));
        Stop("min_progress_rr 10", Set(TimeStopBase, P("min_progress_rr", 10)));
        Stop("min_progress_rr 10.1", Set(TimeStopBase, P("min_progress_rr", 10.1)), Range + O + "min_progress_rr");
        Stop("min_progress_rr two values", Set(TimeStopBase, P("min_progress_rr", 0.5, 1)), Shape + O + "min_progress_rr");
        Stop("intraday_bars 0", Set(TimeStopBase, P("intraday_bars", 0)), Range + O + "intraday_bars");
        Stop("intraday_bars 1", Set(TimeStopBase, P("intraday_bars", 1)));
        Stop("intraday_bars 6.5", Set(TimeStopBase, P("intraday_bars", 6.5)), Shape + O + "intraday_bars");
        Stop("intraday_bars 25", Set(TimeStopBase, P("intraday_bars", 25)));
        Stop("intraday_bars 26", Set(TimeStopBase, P("intraday_bars", 26)), Range + O + "intraday_bars");
        Stop("intraday_bars 376", Set(TimeStopBase, P("intraday_bars", 376)), Range + O + "intraday_bars");
        Stop("intraday_bars two values", Set(TimeStopBase, P("intraday_bars", 6, 7)), Shape + O + "intraday_bars");
        Stop("the dropped field enabled", [.. TimeStopBase, P("enabled", 1)], NotUsed + O + "enabled");
        Stop("the provisional name minutes", [.. TimeStopBase, P("minutes", 30)], NotUsed + O + "minutes");
        foreach (var (timeframe, most) in new[] { (Timeframe.M5, 75), (Timeframe.M10, 38), (Timeframe.M30, 13), (Timeframe.H1, 7) })
        {
            AddOn("time_stop intraday_bars " + most + " on " + timeframe.ToCode(), Overlay(OverlayKind.TimeStop, Set(TimeStopBase, P("intraday_bars", most)), timeframe), timeframe);
            AddOn("time_stop intraday_bars " + (most + 1) + " on " + timeframe.ToCode(), Overlay(OverlayKind.TimeStop, Set(TimeStopBase, P("intraday_bars", most + 1)), timeframe), timeframe, Range + O + "intraday_bars");
        }

        // time_stop of a swing strategy
        AddOn("swing time_stop valid", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2)], 5), Timeframe.H1);
        AddOn("swing time_stop on 1D", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2)], 5, Timeframe.D1), Timeframe.D1);
        AddOn("swing time_stop swing_sessions 4 with max_hold_sessions 5", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 4)], 5), Timeframe.H1);
        AddOn("swing time_stop swing_sessions 5 with max_hold_sessions 5", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 5)], 5), Timeframe.H1, Range + O + "swing_sessions");
        AddOn("swing time_stop swing_sessions 6 is one error, the range of the field", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 6)], 5), Timeframe.H1, Range + O + "swing_sessions");
        AddOn("swing time_stop swing_sessions 0", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 0)], 5), Timeframe.H1, Range + O + "swing_sessions");
        AddOn("swing time_stop swing_sessions 2.5", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2.5)], 5), Timeframe.H1, Shape + O + "swing_sessions");
        AddOn("swing time_stop swing_sessions 1 with max_hold_sessions 2", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 1)], 2), Timeframe.H1);
        AddOn("swing time_stop swing_sessions 2 with max_hold_sessions 2", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2)], 2), Timeframe.H1, Range + O + "swing_sessions");
        AddOn("swing time_stop swing_sessions 1 with max_hold_sessions 1", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 1)], 1), Timeframe.H1, Range + O + "swing_sessions");
        AddOn("swing time_stop with max_hold_sessions absent adds no second problem", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2)], null), Timeframe.H1, "model.time_rule_missing@time_rules.max_hold_sessions");
        AddOn("swing time_stop with max_hold_sessions 9 adds no second problem", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2)], 9), Timeframe.H1, "model.max_hold_out_of_range@time_rules.max_hold_sessions");
        AddOn("swing time_stop with intraday_bars", SwingStop([P("min_progress_rr", 0.5), P("intraday_bars", 6)], 5), Timeframe.H1, NotUsed + O + "intraday_bars", Missing + O + "swing_sessions");
        AddOn("swing time_stop with both", SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2), P("intraday_bars", 6)], 5), Timeframe.H1, NotUsed + O + "intraday_bars");
        AddOn("swing time_stop with neither", SwingStop([P("min_progress_rr", 0.5)], 5), Timeframe.H1, Missing + O + "swing_sessions");

        // event_exit
        Event("valid", [P("days_before_results", 1)]);
        Event("days_before_results missing", [], Missing + O + "days_before_results");
        Event("days_before_results 0", [P("days_before_results", 0)], Range + O + "days_before_results");
        Event("days_before_results 11", [P("days_before_results", 11)], Range + O + "days_before_results");
        Event("days_before_results 10", [P("days_before_results", 10)]);
        Event("days_before_results 1.5", [P("days_before_results", 1.5)], Shape + O + "days_before_results");
        Event("days_before_results two values", [P("days_before_results", 1, 2)], Shape + O + "days_before_results");
        Event("the dropped field enabled", [P("days_before_results", 1), P("enabled", 1)], NotUsed + O + "enabled");

        var data = new TheoryData<string, StrategyDefinition, Timeframe, string[]>();
        foreach (var row in rows)
        {
            data.Add(row.Name, row.Definition, row.Timeframe, row.Errors);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_row_of_a_table_gives_exactly_its_errors_at_its_paths_in_order(string name, StrategyDefinition definition, Timeframe timeframe, string[] errors)
    {
        var result = Located.Compile(definition, timeframe);

        result.ErrorList().ShouldBe(errors, name + "\n" + Located.Show(result));
    }

    // ---- criterion 1 and 10: the valid examples and the plans ---------------------------------------------------------------------------------------------------------------------------------

    private static void ForBothSides(Func<Side, SideRules, SideRules> change, Action<Side, CompiledSide> assert)
    {
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var definition = Intraday().WithBuy(b => change(Side.Buy, b)).WithSell(s => change(Side.Sell, s));

            assert(side, Located.Ok(definition, Timeframe.M15, side));
        }
    }

    private static FeatureSpec Atr(int period = 14) => Specs.Of("atr", Timeframe.M15, period);

    [Fact]
    public void PARTIAL_BE_compiles_on_both_sides_to_its_numbers_and_reads_nothing()
    {
        ForBothSides((_, r) => r with { Exits = [new ExitSpec(ExitPolicy.PartialBe, BeBase)] }, (_, side) =>
        {
            var plan = side.Exits.ShouldHaveSingleItem();
            plan.Values.Keys.ShouldBe(["book_pct", "t1_rr", "t2_rr"], ignoreOrder: true);
            plan.Values["book_pct"].ShouldBe([50]);
            plan.Values["t1_rr"].ShouldBe([1]);
            plan.Values["t2_rr"].ShouldBe([2, 3]);
            plan.Reads.ShouldBeEmpty();
        });
    }

    [Fact]
    public void ATR_TRAIL_compiles_on_both_sides_to_its_numbers_with_atr_period_14_in_the_plan_and_reads_atr_14()
    {
        ForBothSides((_, r) => r with { Exits = [new ExitSpec(ExitPolicy.AtrTrail, AtrBase)] }, (_, side) =>
        {
            var plan = side.Exits.ShouldHaveSingleItem();
            plan.Values["mult"].ShouldBe([2, 3]);
            plan.Values["activate_at_rr"].ShouldBe([1]);
            plan.Values["atr_period"].ShouldBe([14], "the default is part of the plan");
            plan.Reads.ShouldBe([Atr()]);
        });
    }

    [Fact]
    public void STRUCTURE_TRAIL_reads_the_lowest_low_for_BUY_and_the_highest_high_for_SELL()
    {
        ForBothSides((_, r) => r with { Exits = [new ExitSpec(ExitPolicy.StructureTrail, StructBase)] }, (s, side) =>
        {
            var plan = side.Exits.ShouldHaveSingleItem();
            plan.Values["lookback_bars"].ShouldBe([3]);
            plan.Reads.ShouldBe([s == Side.Buy ? Specs.Of("lowest", Timeframe.M15, 3, source: PriceSource.Low) : Specs.Of("highest", Timeframe.M15, 3, source: PriceSource.High)]);
        });
    }

    [Fact]
    public void EMA_TRAIL_reads_the_ema_of_the_close()
    {
        ForBothSides((_, r) => r with { Exits = [new ExitSpec(ExitPolicy.EmaTrail, EmaBase)] }, (_, side) =>
        {
            var plan = side.Exits.ShouldHaveSingleItem();
            plan.Values["length"].ShouldBe([20]);
            plan.Reads.ShouldBe([Specs.Of("ema", Timeframe.M15, 20)]);
        });
    }

    [Fact]
    public void PYRAMID_compiles_on_both_sides_to_its_numbers_and_reads_atr_14()
    {
        ForBothSides((_, r) => r with { Exits = [new ExitSpec(ExitPolicy.Pyramid, PyramidBase)] }, (_, side) =>
        {
            var plan = side.Exits.ShouldHaveSingleItem();
            plan.Values["initial_pct"].ShouldBe([50]);
            plan.Values["add_at_rr"].ShouldBe([1, 2]);
            plan.Values["add_pct"].ShouldBe([30, 20]);
            plan.Values["max_adds"].ShouldBe([2]);
            plan.Values["trail_atr_mult"].ShouldBe([2]);
            plan.Values["atr_period"].ShouldBe([14]);
            plan.Reads.ShouldBe([Atr()]);
        });
    }

    [Fact]
    public void The_overlays_compile_on_both_sides_to_their_numbers_and_read_nothing()
    {
        ForBothSides((_, r) => r with { Overlays = [new OverlaySpec(OverlayKind.TimeStop, TimeStopBase), new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 1)])] }, (_, side) =>
        {
            side.Overlays[0].Values["min_progress_rr"].ShouldBe([0.5]);
            side.Overlays[0].Values["intraday_bars"].ShouldBe([6]);
            side.Overlays[1].Values["days_before_results"].ShouldBe([1]);
            side.Features.ShouldBe(side.Features.Distinct().ToList(), "no overlay adds a feature");
        });
    }

    [Fact]
    public void A_written_atr_period_is_in_the_plan_as_written_and_two_compiles_of_one_definition_give_equal_plans()
    {
        var definition = Exit(ExitPolicy.AtrTrail, [.. AtrBase, P("atr_period", 10)]);

        var first = Located.Ok(definition).Exits.Single();
        var second = Located.Ok(definition).Exits.Single();

        first.Values["atr_period"].ShouldBe([10]);
        first.Reads.ShouldBe([Atr(10)]);
        second.Policy.ShouldBe(first.Policy);
        second.Reads.ShouldBe(first.Reads);
        second.Values.Keys.Order().ShouldBe(first.Values.Keys.Order());
        second.Values.Keys.ShouldAllBe(k => second.Values[k].SequenceEqual(first.Values[k]));
    }

    [Fact]
    public void A_named_parameter_resolves_before_the_range_and_the_relations_are_checked()
    {
        var threeLevels = new[] { P("initial_pct", 40), P("add_at_rr", 1, 2, 3), P("add_pct", 30, 20, 10), new ExitParameter("max_adds", [Quantity.Named("aa")]), P("trail_atr_mult", 2) };

        var side = Located.Ok(Exit(ExitPolicy.Pyramid, threeLevels));
        var tooMany = Located.Compile(Exit(ExitPolicy.Pyramid, Set(PyramidBase, new ExitParameter("max_adds", [Quantity.Named("aa")]))));

        side.Exits.Single().Values["max_adds"].ShouldBe([3]);
        tooMany.ErrorList().ShouldBe([Range + X + "max_adds"]);
    }

    // ---- messages -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_sum_of_the_percentages_is_in_the_message_and_the_dropped_fields_say_why()
    {
        var sum = Located.Compile(Exit(ExitPolicy.Pyramid, Set(PyramidBase, P("add_pct", 30, 21))));
        var trail = Located.Compile(Exit(ExitPolicy.Pyramid, [.. PyramidBase, P("trail", 1)]));
        var cap = Located.Compile(Exit(ExitPolicy.Pyramid, [.. PyramidBase, P("cap_total_risk_to_initial", 1)]));
        var enabled = Located.Compile(Overlay(OverlayKind.EventExit, [P("days_before_results", 1), P("enabled", 1)]));

        sum.ShouldHave(ModelCodes.ValueOutOfRange, X + "add_pct").Problem.Message.ShouldContain("101");
        trail.ShouldHave(ModelCodes.FieldNotUsed, X + "trail").Problem.Message.ShouldContain("trail_atr_mult");
        cap.ShouldHave(ModelCodes.FieldNotUsed, X + "cap_total_risk_to_initial").Problem.Message.ShouldContain("always on");
        enabled.ShouldHave(ModelCodes.FieldNotUsed, O + "enabled").Problem.Message.ShouldContain("is on");
    }

    [Fact]
    public void A_time_stop_with_max_hold_sessions_1_says_there_is_nothing_to_time_out()
    {
        var result = Located.Compile(SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 1)], 1), Timeframe.H1);

        result.ShouldHave(ModelCodes.ValueOutOfRange, O + "swing_sessions").Problem.Message.ShouldContain("one session");
    }

    // ---- criterion 7: the style reaches the exits for both sides, and a swing SELL is not reachable ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_style_rule_is_the_same_for_BUY_and_SELL()
    {
        var overlay = new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("swing_sessions", 2)]);
        var definition = Intraday().WithBuy(b => b with { Overlays = [overlay] }).WithSell(s => s with { Overlays = [overlay] });

        Located.Compile(definition, Timeframe.M15, Side.Buy).ErrorList().ShouldBe([NotUsed + O + "swing_sessions", Missing + O + "intraday_bars"]);
        Located.Compile(definition, Timeframe.M15, Side.Sell).ErrorList().ShouldBe([NotUsed + "sell.overlays[0].swing_sessions", Missing + "sell.overlays[0].intraday_bars"]);
    }

    [Fact]
    public void A_swing_strategy_that_lists_SELL_has_only_swing_sell_unsupported_and_the_time_stop_adds_none()
    {
        var swing = SwingStop([P("min_progress_rr", 0.5), P("swing_sessions", 2)], 5) with { Sides = [Side.Buy, Side.Sell] };
        swing = swing with { Sell = swing.Buy! with { Stop = new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Named("lookback_days")) } };

        var result = Located.Compile(swing, Timeframe.H1, Side.Sell);

        result.ErrorList().ShouldBe(["model.swing_sell_unsupported@sides[1]"]);
    }

    [Fact]
    public void A_fault_on_one_side_never_changes_the_result_of_the_other()
    {
        var bad = new ExitSpec(ExitPolicy.PartialBe, Set(BeBase, P("t2_rr", 0.5)));
        var other = new ExitSpec(ExitPolicy.Pyramid, Set(PyramidBase, P("max_adds", 5)));
        var definition = Intraday().WithBuy(b => b with { Exits = [bad] }).WithSell(s => s with { Exits = [other] });

        Located.Compile(definition, Timeframe.M15, Side.Buy).ErrorList().ShouldBe([Range + X + "t2_rr[0]"]);
        Located.Compile(definition, Timeframe.M15, Side.Sell).ErrorList().ShouldBe([Range + "sell.exits[0].max_adds"]);
    }

    // ---- criterion 8: exit_not_evaluated ------------------------------------------------------------------------------------------------------------------------------------------------------

    private static ExitParameter[] ValidFor(ExitPolicy policy) => policy switch
    {
        ExitPolicy.FixedRr => [P("rr", 2)],
        ExitPolicy.PartialBe => BeBase,
        ExitPolicy.PartialTrail => [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2)],
        ExitPolicy.AtrTrail => AtrBase,
        ExitPolicy.StructureTrail => StructBase,
        ExitPolicy.EmaTrail => EmaBase,
        _ => PyramidBase,
    };

    [Fact]
    public void The_policies_and_the_time_stop_the_simulator_runs_have_no_such_warning_and_event_exit_has_one()
    {
        Located.Compile(Intraday()).Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.ExitNotEvaluated);

        var result = Located.Compile(Intraday().WithBuy(b => b with { Overlays = [new OverlaySpec(OverlayKind.TimeStop, TimeStopBase), new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 1)])] }));

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        result.Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.ExitNotEvaluated && p.Path == "buy.overlays[0]");
        var eventExit = result.ShouldHave(ModelCodes.ExitNotEvaluated, "buy.overlays[1]", ProblemSeverity.Warning);
        eventExit.Problem.Message.ShouldContain("P15");
    }

    [Fact]
    public void A_pyramid_with_a_table_error_gives_its_errors_and_no_warning_since_the_simulator_runs_it()
    {
        var result = Located.Compile(Exit(ExitPolicy.Pyramid, Set(PyramidBase, P("max_adds", 11))));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorList().ShouldBe([Range + X + "max_adds"]);
        result.Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.ExitNotEvaluated);
    }

    [Fact]
    public void Every_policy_is_run_by_the_simulator_so_none_is_given_the_warning_and_only_event_exit_is()
    {
        ExitTables.RunBySimulator.ShouldBe(Enum.GetValues<ExitPolicy>(), ignoreOrder: true);
        ExitTables.OverlaysRunBySimulator.ShouldBe([OverlayKind.TimeStop]);
        foreach (var policy in Enum.GetValues<ExitPolicy>())
        {
            Located.Compile(Exit(policy, ValidFor(policy))).Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.ExitNotEvaluated, policy.ToString());
        }
    }

    // ---- criterion 9: the features and the look-ahead rules --------------------------------------------------------------------------------------------------------------------------------

    private static StrategyDefinition AllCandidates(Timeframe timeframe)
    {
        var daily = timeframe == Timeframe.D1;
        var exits = new[]
        {
            Defs.FixedRr(2),
            new ExitSpec(ExitPolicy.PartialBe, BeBase),
            new ExitSpec(ExitPolicy.PartialTrail, ValidFor(ExitPolicy.PartialTrail)),
            new ExitSpec(ExitPolicy.AtrTrail, AtrBase),
            new ExitSpec(ExitPolicy.StructureTrail, StructBase),
            new ExitSpec(ExitPolicy.EmaTrail, EmaBase),
            new ExitSpec(ExitPolicy.Pyramid, PyramidBase),
        };
        var overlays = new[]
        {
            new OverlaySpec(OverlayKind.TimeStop, daily ? [P("min_progress_rr", 0.5), P("swing_sessions", 2)] : TimeStopBase),
            new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 1)]),
        };
        var definition = daily ? Defs.Swing() : Intraday(timeframe).WithSell(s => s with { Exits = exits, Overlays = overlays });

        return definition.WithBuy(b => b with { Exits = exits, Overlays = overlays });
    }

    public static TheoryData<Timeframe, Side> TimeframesAndSides { get; } = new()
    {
        { Timeframe.M5, Side.Buy }, { Timeframe.M5, Side.Sell },
        { Timeframe.M10, Side.Buy }, { Timeframe.M10, Side.Sell },
        { Timeframe.M15, Side.Buy }, { Timeframe.M15, Side.Sell },
        { Timeframe.M30, Side.Buy }, { Timeframe.M30, Side.Sell },
        { Timeframe.H1, Side.Buy }, { Timeframe.H1, Side.Sell },
        { Timeframe.D1, Side.Buy },
    };

    [Theory]
    [MemberData(nameof(TimeframesAndSides))]
    public void Every_feature_an_exit_reads_is_on_the_evaluation_timeframe_and_the_frame_of_the_side_is_created_with_handles_in_order(Timeframe timeframe, Side side)
    {
        var compiled = Located.Ok(AllCandidates(timeframe), timeframe, side);

        compiled.Exits.SelectMany(e => e.Reads).ShouldAllBe(r => r.Timeframe == timeframe);
        compiled.Exits.Count(e => e.Reads.Count > 0).ShouldBe(5, "PARTIAL_TRAIL, ATR_TRAIL, STRUCTURE_TRAIL, EMA_TRAIL and PYRAMID read one feature each");
        var frame = FeatureFrame.Create(compiled.Features, new FrameOptions(null, Math.Max(1, compiled.HistoryDepth)));
        frame.IsSuccess.ShouldBeTrue();
        for (var i = 0; i < compiled.Features.Count; i++)
        {
            frame.Value.HandleOf(compiled.Features[i]).ShouldBe(i);
        }
    }

    [Fact]
    public void The_features_of_the_exits_come_after_the_stop_in_the_order_of_the_exits_and_shared_ones_appear_once()
    {
        var definition = Intraday().WithBuy(b => b with
        {
            Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5)),
            Exits =
            [
                new ExitSpec(ExitPolicy.AtrTrail, AtrBase),
                new ExitSpec(ExitPolicy.Pyramid, PyramidBase),
                new ExitSpec(ExitPolicy.StructureTrail, [P("lookback_bars", 5)]),
                new ExitSpec(ExitPolicy.PartialTrail, [.. ValidFor(ExitPolicy.PartialTrail), P("atr_period", 10)]),
                new ExitSpec(ExitPolicy.EmaTrail, EmaBase),
            ],
        });

        var side = Located.Ok(definition);
        var features = side.Features.ToList();

        side.Features.Count(f => f == Atr()).ShouldBe(1, "ATR_TRAIL and PYRAMID with atr_period 14 share one atr(14)");
        side.Features.Count(f => f == Specs.Of("lowest", Timeframe.M15, 5, source: PriceSource.Low)).ShouldBe(1, "STRUCTURE_TRAIL 5 and a swing_low stop with lookback 5 share one lowest(5, source=low)");
        side.Features.Count(f => f.Name == "atr").ShouldBe(2, "PARTIAL_TRAIL with atr_period 10 and ATR_TRAIL with 14 are two");
        features.IndexOf(Specs.Of("lowest", Timeframe.M15, 5, source: PriceSource.Low)).ShouldBeLessThan(features.IndexOf(Atr()), "the stop's features come before the exits'");
        features.IndexOf(Atr()).ShouldBeLessThan(features.IndexOf(Atr(10)));
        features.IndexOf(Atr(10)).ShouldBeLessThan(features.IndexOf(Specs.Of("ema", Timeframe.M15, 20)));
    }

    [Fact]
    public void On_the_SELL_side_STRUCTURE_TRAIL_and_a_swing_high_stop_share_one_highest()
    {
        var definition = Intraday().WithSell(s => s with
        {
            Stop = new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Of(5)),
            Exits = [new ExitSpec(ExitPolicy.StructureTrail, [P("lookback_bars", 5)])],
        });

        var side = Located.Ok(definition, Timeframe.M15, Side.Sell);

        side.Features.Count(f => f == Specs.Of("highest", Timeframe.M15, 5, source: PriceSource.High)).ShouldBe(1);
    }

    [Fact]
    public void A_catalog_refusal_of_a_period_is_reported_once_at_the_field_and_builds_no_second_problem()
    {
        var result = Located.Compile(Exit(ExitPolicy.AtrTrail, [.. AtrBase, P("atr_period", 0)]));

        result.ErrorList().ShouldBe([Invalid + X + "atr_period"]);
    }
}
