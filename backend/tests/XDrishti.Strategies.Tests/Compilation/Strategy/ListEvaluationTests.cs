using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Criteria 19 to 21: the fold of the truths of a list, the equality of list-level evaluation with the one joined condition, and the decision of the three lists.</summary>
public sealed class ListEvaluationTests
{
    private static readonly Truth[] Values = [Truth.False, Truth.True, Truth.Unknown];

    // ---- criterion 19: the fold ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Kleene's AND and OR over a list, written from the tables and not from the library: a false decides AND (a true decides OR), else an unknown makes it unknown.</summary>
    private static Truth Expected(IReadOnlyList<Truth> items, ListMode mode)
    {
        if (mode == ListMode.All)
        {
            return items.Contains(Truth.False) ? Truth.False : items.Contains(Truth.Unknown) ? Truth.Unknown : Truth.True;
        }

        return items.Contains(Truth.True) ? Truth.True : items.Contains(Truth.Unknown) ? Truth.Unknown : Truth.False;
    }

    private static IEnumerable<Truth[]> Assignments(int length)
    {
        var total = (int)Math.Pow(3, length);
        for (var n = 0; n < total; n++)
        {
            var items = new Truth[length];
            var rest = n;
            for (var i = 0; i < length; i++)
            {
                items[i] = Values[rest % 3];
                rest /= 3;
            }

            yield return items;
        }
    }

    private static List<string> Mismatches(Func<Truth[], ListMode, Truth> fold)
    {
        var wrong = new List<string>();
        foreach (var mode in new[] { ListMode.All, ListMode.Any })
        {
            for (var length = 0; length <= 4; length++)
            {
                foreach (var items in Assignments(length))
                {
                    if (fold(items, mode) != Expected(items, mode))
                    {
                        wrong.Add(mode + " " + string.Concat(items.Select(t => t switch { Truth.True => 'T', Truth.False => 'F', _ => 'U' })));
                    }
                }
            }
        }

        return wrong;
    }

    [Fact]
    public void The_fold_equals_the_kleene_tables_for_every_assignment_of_one_to_four_items_and_for_none()
    {
        // 3 + 9 + 27 + 81 assignments, and the fold of none: all is true, any is false
        Assignments(1).Count().ShouldBe(3);
        Assignments(2).Count().ShouldBe(9);
        Assignments(3).Count().ShouldBe(27);
        Assignments(4).Count().ShouldBe(81);

        Mismatches((items, mode) => ListFold.Fold(items, mode)).ShouldBeEmpty();
        ListFold.Fold([], ListMode.All).ShouldBe(Truth.True);
        ListFold.Fold([], ListMode.Any).ShouldBe(Truth.False);
    }

    [Fact]
    public void A_mutant_that_folds_all_as_any_or_any_as_all_fails_the_check()
    {
        Mismatches((items, mode) => ListFold.Fold(items, mode == ListMode.All ? ListMode.Any : ListMode.All)).ShouldNotBeEmpty();
        Mismatches((items, mode) => mode == ListMode.All ? (items.Contains(Truth.True) ? Truth.True : Truth.False) : ListFold.Fold(items, mode)).ShouldNotBeEmpty("an unknown treated as false is a mutant too");
        Mismatches((items, mode) => ListFold.Fold(items.Where(t => t != Truth.Unknown).ToArray(), mode)).ShouldNotBeEmpty("so is dropping the unknowns");
    }

    // ---- criterion 20: list level equals the joined text ----------------------------------------------------------------------------------------------------------------------------------

    private static readonly Timeframe[] Evaluations = [Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    [Fact]
    public void For_200_seeds_the_truth_and_the_decision_of_a_list_equal_those_of_the_one_joined_condition_and_each_item_equals_the_item_compiled_alone()
    {
        var unknownComparisons = 0;
        var comparisons = 0;
        var skippedTooDeep = 0;
        var trueDecisions = 0;
        var parameters = ModelGenerator.Parameters.Select(p => new StrategyParam(p.Key, p.Value)).ToList();
        for (var seed = 1; seed <= 200; seed++)
        {
            var evaluation = Evaluations[seed % Evaluations.Length];
            var mode = seed % 2 == 0 ? ListMode.All : ListMode.Any;
            var generator = new ModelGenerator(seed, evaluation, false, false);
            var count = 2 + seed % 5;
            var items = Enumerable.Range(0, count).Select(_ => Printer.Print(generator.Next())).ToList();
            var joinedText = string.Join(mode == ListMode.All ? " and " : " or ", items.Select(i => "(" + i + ")"));
            var joined = ConditionCompiler.Compile(joinedText, new CompileContext(evaluation, ModelGenerator.Parameters));
            if (!joined.IsSuccess)
            {
                joined.Problems.ShouldContain(p => p.Code == XDrishti.Strategies.Syntax.SyntaxCodes.TooDeep, "the joined text of seed " + seed + " fails for another reason than its depth");
                skippedTooDeep++;
                continue;
            }

            var definition = Defs.Basic().WithBuy(b => b with { Trigger = new ConditionList(mode, items) }) with { Params = parameters, Timeframes = [evaluation], Style = Style.Intraday };
            var side = Located.Ok(definition, evaluation);
            var rig = SideRig.Of(side);
            var bound = joined.Condition!.Bind(rig.Frame);
            var alone = items.Select(i => Compiling.Condition(i, evaluation, ModelGenerator.Parameters).Bind(rig.Frame)).ToList();

            foreach (var bar in OracleSeries.For(seed, evaluation, light: true))
            {
                rig.Frame.Step(bar);
                if (!rig.Frame.BarClosed(evaluation))
                {
                    continue;
                }

                var evaluated = rig.Evaluate();
                var truth = bound.EvaluateTruth(rig.Frame);
                evaluated.Trigger.ShouldBe(truth, "seed " + seed + " " + joinedText);
                evaluated.Fires.ShouldBe(bound.Evaluate(rig.Frame), "the decision of seed " + seed);
                for (var k = 0; k < alone.Count; k++)
                {
                    rig.Items[k].ShouldBe(alone[k].EvaluateTruth(rig.Frame), "item " + k + " of seed " + seed);
                }

                comparisons++;
                unknownComparisons += truth == Truth.Unknown || rig.Items.Contains(Truth.Unknown) ? 1 : 0;
                trueDecisions += evaluated.Fires ? 1 : 0;
            }
        }

        comparisons.ShouldBeGreaterThan(5_000);
        unknownComparisons.ShouldBeGreaterThan(0, "the unknown results compared");
        trueDecisions.ShouldBeGreaterThan(0, "the comparison is not only of lists that never fire");
        skippedTooDeep.ShouldBeLessThan(40, "most joined texts are within the depth limit and can be compared");
    }

    // ---- criterion 21: the decision -----------------------------------------------------------------------------------------------------------------------------------------------------------------

    private const string True = "close > 0";

    private const string False = "close < 0";

    private const string Unknown = "rsi(14) > 0";

    private static string? Item(char letter) => letter switch { 'T' => True, 'F' => False, 'U' => Unknown, _ => null };

    private static ConditionList? List(char letter) => Item(letter) is { } item ? Defs.All(item) : null;

    private static Truth TruthOf(char letter) => letter switch { 'T' => Truth.True, 'F' => Truth.False, _ => Truth.Unknown };

    public static TheoryData<char, char, char> Cases { get; } = [.. "-TFU".SelectMany(setup => "TFU".SelectMany(trigger => "-TFU".Select(invalidate => (setup, trigger, invalidate))))];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Fires_is_true_exactly_when_setup_is_absent_or_true_trigger_is_true_and_invalidate_is_absent_or_false(char setup, char trigger, char invalidate)
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = List(setup), Trigger = List(trigger), Invalidate = List(invalidate) });
        var rig = SideRig.Of(Located.Ok(definition));
        for (var minute = Clock.Open; minute < Clock.Open + 15; minute++)
        {
            rig.Frame.Step(Clock.Close(Clock.Mon, minute, 100));
        }

        rig.Frame.BarClosed(Timeframe.M15).ShouldBeTrue();
        if ($"{setup}{trigger}{invalidate}".Contains('U', StringComparison.Ordinal))
        {
            rig.Frame.IsReady(rig.Frame.HandleOf(Specs.Rsi(Timeframe.M15, 14))).ShouldBeFalse("the RSI is not ready: the unknown item is unknown");
        }

        var evaluation = rig.Evaluate();

        evaluation.Setup.ShouldBe(setup == '-' ? Truth.True : TruthOf(setup));
        evaluation.Trigger.ShouldBe(TruthOf(trigger));
        evaluation.Invalidate.ShouldBe(invalidate == '-' ? Truth.False : TruthOf(invalidate));
        evaluation.Fires.ShouldBe((setup is '-' or 'T') && trigger == 'T' && (invalidate is '-' or 'F'), $"setup {setup} trigger {trigger} invalidate {invalidate}");
        evaluation.Invalidated.ShouldBe(invalidate == 'T');
        rig.Items.Count.ShouldBe((setup == '-' ? 0 : 1) + 1 + (invalidate == '-' ? 0 : 1));
    }

    [Fact]
    public void An_unknown_invalidation_neither_fires_nor_invalidates()
    {
        var unknown = new SideEvaluation(Truth.True, Truth.True, Truth.Unknown);

        unknown.Fires.ShouldBeFalse();
        unknown.Invalidated.ShouldBeFalse();
        new SideEvaluation(Truth.True, Truth.True, Truth.False).Fires.ShouldBeTrue();
        new SideEvaluation(Truth.Unknown, Truth.True, Truth.False).Fires.ShouldBeFalse();
    }

    [Fact]
    public void Every_item_is_evaluated_at_every_call_and_an_any_invalidate_list_is_a_kleene_or()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All(False, True, Unknown), Trigger = Defs.Any(Unknown, True), Invalidate = Defs.Any(False, Unknown) });
        var rig = SideRig.Of(Located.Ok(definition));
        for (var minute = Clock.Open; minute < Clock.Open + 15; minute++)
        {
            rig.Frame.Step(Clock.Close(Clock.Mon, minute, 100));
        }

        var evaluation = rig.Evaluate();

        rig.Word.ShouldBe("FTUUTFU", "setup, trigger and invalidate items in the order of the model, none skipped");
        evaluation.ShouldBe(new SideEvaluation(Truth.False, Truth.True, Truth.Unknown));
    }

    [Fact]
    public void A_span_shorter_than_the_items_and_a_null_frame_are_programming_errors()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Trigger = Defs.All(True, False) });
        var rig = SideRig.Of(Located.Ok(definition));

        Should.Throw<ArgumentException>(() => rig.Bound.Evaluate(rig.Frame, new Truth[1]));
        Should.Throw<ArgumentNullException>(() => rig.Bound.Evaluate(null!, new Truth[2]));
        Should.Throw<ArgumentNullException>(() => rig.Bound.InEntryWindow(null!));
        rig.Bound.Evaluate(rig.Frame, new Truth[5]).Trigger.ShouldBe(Truth.Unknown, "a longer span is fine; before the first bar the close is not known");
    }

    [Fact]
    public void A_side_bound_to_a_second_frame_with_the_same_layout_gives_the_same_answers()
    {
        var side = Located.Ok(Defs.VwapRsiTrend());
        var first = SideRig.Of(side);
        var second = SideRig.Of(side, extraDepth: 3);

        foreach (var bar in Series.Sessions(7, 1).Take(120))
        {
            first.Step(bar).ShouldBe(second.Step(bar));
        }
    }

    [Fact]
    public void Compile_keeps_the_side_for_both_sides_of_a_definition_separate()
    {
        var buy = Located.Ok(Defs.Orb(), Timeframe.M15, Side.Buy);
        var sell = Located.Ok(Defs.Orb(), Timeframe.M15, Side.Sell);

        buy.Side.ShouldBe(Side.Buy);
        sell.Side.ShouldBe(Side.Sell);
        buy.Trigger.Items[0].Condition.Source.ShouldNotBe(sell.Trigger.Items[0].Condition.Source);
    }
}
