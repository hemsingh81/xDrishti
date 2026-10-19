using System.Reflection;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Criterion 29: the compiler warm-up covers the strategy compiler. The sets of definitions are valid and invalid as they say, <c>Run</c> compiles and binds every valid one and refuses every invalid one, and between them
/// they use every entry kind, stop kind, exit policy and overlay, both sides and both styles, and reach every group of <c>strategy.model.*</c> codes; a new kind or group fails these tests until the warm-up covers it. The
/// time budgets (150 ms for the warm-up, 20 ms for the first strategy compile after it) are explicit benchmarks, not asserted here.
/// </summary>
public sealed class StrategyWarmupTests
{
    private static readonly string[][] Groups =
    [
        ["schema_unsupported", "bad_id", "undefined_value", "timeframes_empty", "bad_timeframe", "duplicate_timeframe", "timeframe_not_declared", "style_timeframe_mismatch", "sides_empty", "duplicate_side", "side_not_declared", "side_rules_missing", "sell_block_missing", "swing_sell_unsupported", "side_rules_unused"],
        ["too_many_params", "duplicate_param", "bad_param_name", "bad_param_value", "param_shadows_name", "unknown_param", "bad_quantity"],
        ["trigger_missing", "list_empty", "list_too_long", "entry_missing", "stop_missing", "exits_empty"],
        ["stop_wrong_side", "field_missing", "field_not_used", "field_shape", "value_out_of_range", "feature_invalid", "duplicate_exit", "exit_not_evaluated"],
        ["time_rule_missing", "bad_time", "time_rules_out_of_order", "time_rule_on_daily", "square_off_on_swing", "max_hold_on_intraday", "max_hold_out_of_range", "unknown_filter", "filter_not_evaluated", "duplicate_filter"],
        ["too_many_problems"],
    ];

    private static List<CompiledSide> CompiledSides() =>
        [.. WarmupStrategies.Valid.Select(c => StrategyCompiler.Compile(c.Definition, c.Timeframe, c.Side).Compiled).OfType<CompiledSide>()];

    private static StrategyCompileResult Compile(WarmupStrategies.Case item) => StrategyCompiler.Compile(item.Definition, item.Timeframe, item.Side);

    [Fact]
    public void Every_valid_strategy_compiles_every_invalid_one_is_refused_and_the_run_reports_it()
    {
        var notValid = WarmupStrategies.Valid.Where(c => !Compile(c).IsSuccess).Select(c => c.Definition.Id + " " + c.Timeframe + " " + c.Side + ":\n" + Located.Show(Compile(c))).ToList();
        var notInvalid = WarmupStrategies.Invalid.Where(c => Compile(c).IsSuccess).Select(c => c.Definition.Id + " " + c.Timeframe + " " + c.Side).ToList();

        notValid.ShouldBeEmpty("these strategies of the valid set do not compile, so the warm-up would skip them");
        notInvalid.ShouldBeEmpty("these strategies of the invalid set compile, so they warm up nothing of the error paths");
        var outcome = CompilerWarmup.Warm();
        outcome.StrategiesCompiled.ShouldBe(WarmupStrategies.Valid.Count);
        outcome.StrategiesBound.ShouldBe(WarmupStrategies.Valid.Count, "every compiled side is bound to a frame and evaluated once");
        outcome.StrategiesRefused.ShouldBe(WarmupStrategies.Invalid.Count);
    }

    [Fact]
    public void The_valid_set_uses_every_entry_kind_stop_kind_exit_policy_and_overlay_and_both_sides_and_both_styles()
    {
        var sides = CompiledSides();

        sides.Select(s => s.Entry).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<EntryKind>(), "entry kinds");
        sides.Select(s => s.Stop.Kind).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<StopKind>(), "stop kinds");
        sides.SelectMany(s => s.Exits).Select(e => e.Policy).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<ExitPolicy>(), "exit policies");
        sides.SelectMany(s => s.Overlays).Select(o => o.Kind).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<OverlayKind>(), "overlays");
        sides.Select(s => s.Side).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<Side>());
        sides.Select(s => s.Style).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<Style>());
        sides.Select(s => s.EvaluationTimeframe).Distinct().Count().ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void The_valid_set_uses_both_modes_all_three_lists_a_named_quantity_both_filters_and_the_needs_of_the_frame()
    {
        var sides = CompiledSides();

        sides.Select(s => s.Trigger.Mode).Distinct().Order().ToArray().ShouldBe(Enum.GetValues<ListMode>());
        sides.Count(s => s.Setup is not null).ShouldBeGreaterThan(0);
        sides.Count(s => s.Invalidate is not null).ShouldBeGreaterThan(0);
        sides.SelectMany(s => s.Filters).Distinct().Order().ToArray().ShouldBe(FilterNames.All.Order().ToArray());
        sides.ShouldContain(s => s.NeedsContext);
        sides.ShouldContain(s => s.NeedsExpiryDays);
        sides.SelectMany(s => s.Warnings).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_two_sets_together_reach_every_group_of_model_codes_and_an_error_with_a_path_in_an_item()
    {
        var all = WarmupStrategies.Valid.Concat(WarmupStrategies.Invalid).SelectMany(c => Compile(c).Problems).ToList();
        var codes = all.Select(p => p.Problem.Code).ToHashSet();

        foreach (var group in Groups)
        {
            codes.Intersect(group.Select(c => "strategy.model." + c)).ShouldNotBeEmpty("no code of the group " + group[0] + " ... is reported by the warm-up");
        }

        all.ShouldContain(p => p.Path.StartsWith("buy.setup[", StringComparison.Ordinal) && p.Problem.Code.StartsWith("strategy.lint.", StringComparison.Ordinal), "a fault in an item with its path");
        all.ShouldContain(p => p.Problem.Code == ModelCodes.TooManyProblems && p.Problem.Severity == ProblemSeverity.Error);
        all.ShouldContain(p => p.Problem.Code == ModelCodes.TooManyProblems && p.Problem.Severity == ProblemSeverity.Information);
        all.ShouldContain(p => p.Problem.Code == ModelCodes.StopWrongSide, "a stop on the wrong side");
    }

    [Fact]
    public void The_groups_of_this_test_are_all_the_codes_the_model_has()
    {
        var codes = typeof(ModelCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Groups.SelectMany(g => g).Select(c => "strategy.model." + c).Order().ShouldBe(codes.Order(), "a new code belongs to a group; add it to the groups here");
    }

    [Fact]
    public void The_two_sets_have_no_strategy_twice_so_that_every_case_warms_something_up()
    {
        string Key(WarmupStrategies.Case c) => c.Definition.Id + "|" + c.Timeframe + "|" + c.Side + "|" + c.Definition.GetHashCode();

        WarmupStrategies.Valid.Concat(WarmupStrategies.Invalid).Select(Key).ShouldBeUnique();
    }

    [Fact]
    public void The_valid_strategies_are_not_the_example_definitions_the_benchmarks_and_the_tests_compile()
    {
        var warm = WarmupStrategies.Valid.Select(c => c.Definition).ToList();

        warm.ShouldNotContain(Defs.VwapRsiTrend());
        warm.ShouldNotContain(Defs.Orb());
    }
}
