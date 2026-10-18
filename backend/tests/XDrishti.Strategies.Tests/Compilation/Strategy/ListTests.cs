using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Criteria 8 and 9: the lists, and every item compiled alone with its path and the span inside the item.</summary>
public sealed class ListTests
{
    private static StrategyDefinition Basic() => Defs.Basic();

    private static StrategyDefinition WithTrigger(params string[] items) => Basic().WithBuy(b => b with { Trigger = Defs.All(items) });

    [Fact]
    public void A_missing_trigger_gives_trigger_missing_and_a_present_list_with_no_items_gives_list_empty()
    {
        Located.Compile(Basic().WithBuy(b => b with { Trigger = null })).ErrorList().ShouldBe(["model.trigger_missing@buy.trigger"]);
        Located.Compile(Basic().WithBuy(b => b with { Trigger = Defs.All() })).ErrorList().ShouldBe(["model.list_empty@buy.trigger"]);
        Located.Compile(Basic().WithBuy(b => b with { Setup = Defs.Any() })).ErrorList().ShouldBe(["model.list_empty@buy.setup"]);
        Located.Compile(Basic().WithBuy(b => b with { Invalidate = Defs.All() })).ErrorList().ShouldBe(["model.list_empty@buy.invalidate"]);
        Located.Compile(Basic().WithBuy(b => b with { Setup = new ConditionList(ListMode.All, null!) })).ErrorList().ShouldBe(["model.list_empty@buy.setup"]);
    }

    [Fact]
    public void A_null_setup_or_invalidate_is_no_gate_and_no_invalidation()
    {
        var side = Located.Ok(Basic());

        side.Setup.ShouldBeNull();
        side.Invalidate.ShouldBeNull();
        side.Items.Select(i => i.Path).ShouldBe(["buy.trigger[0]"]);
    }

    [Fact]
    public void A_list_of_exactly_100_items_compiles_and_101_give_list_too_long()
    {
        var hundred = Located.Compile(WithTrigger([.. Enumerable.Repeat("rsi(14) > 55", 100)]));
        var more = Located.Compile(WithTrigger([.. Enumerable.Repeat("rsi(14) > 55", 101)]));

        hundred.IsSuccess.ShouldBeTrue(Located.Show(hundred));
        more.ErrorList().ShouldBe(["model.list_too_long@buy.trigger"]);
    }

    [Fact]
    public void A_hundred_items_in_an_all_list_compile_while_the_same_conditions_joined_by_and_into_one_text_are_too_deep()
    {
        var items = Enumerable.Repeat("rsi(14) > 55", 100).ToArray();
        var joined = ConditionCompiler.Compile(string.Join(" and ", items), new CompileContext(Timeframe.M15));

        var listed = Located.Compile(WithTrigger(items));

        listed.IsSuccess.ShouldBeTrue(Located.Show(listed));
        listed.Compiled!.Items.Count.ShouldBe(100);
        joined.IsSuccess.ShouldBeFalse();
        joined.Problems.ShouldContain(p => p.Code == SyntaxCodes.TooDeep);
    }

    [Fact]
    public void A_list_mode_that_is_not_all_or_any_gives_undefined_value()
    {
        Located.Compile(Basic().WithBuy(b => b with { Trigger = new ConditionList((ListMode)3, ["close > 1"]) })).ShouldHave(ModelCodes.UndefinedValue, "buy.trigger.mode");
    }

    [Fact]
    public void Each_item_is_compiled_alone_so_the_problem_has_the_span_inside_the_item_and_the_path_of_the_item()
    {
        var result = Located.Compile(WithTrigger("crosses_above(close, vwap)", "rsi(14) > 55 and closee > 1"));

        result.ErrorList().ShouldBe(["lint.unknown_name@buy.trigger[1]"]);
        var problem = result.ShouldHave(LintCodes.UnknownName, "buy.trigger[1]").Problem;
        problem.Suggestion.ShouldBe("close");
        problem.Span.ShouldBe(new SourceSpan(17, 23, 1, 18));
    }

    [Fact]
    public void A_type_error_in_an_item_does_not_mention_an_and_the_author_never_wrote()
    {
        var result = Located.Compile(WithTrigger("close > 1", "rsi(14)"));

        var problem = result.ShouldHave(LintCodes.TypeMismatch, "buy.trigger[1]").Problem;
        problem.Message.ShouldNotContain("`and`");
        problem.Message.ShouldNotContain("and");
    }

    [Fact]
    public void Three_faults_in_a_setup_item_a_trigger_item_and_the_stop_are_reported_together_in_the_order_of_the_model()
    {
        var definition = Basic().WithBuy(b => b with
        {
            Setup = Defs.All("close > 1", "rsi(14) > 55", "closee > 1"),
            Trigger = Defs.All("opn < 5"),
            Stop = new StopSpec(StopKind.SwingLow),
        });

        var result = Located.Compile(definition);

        result.ErrorList().ShouldBe(["lint.unknown_name@buy.setup[2]", "lint.unknown_name@buy.trigger[0]", "model.field_missing@buy.stop.lookback"]);
        result.Problems.Select(p => p.Path).ShouldBe(["buy.setup[2]", "buy.trigger[0]", "buy.stop.lookback"]);
    }

    [Fact]
    public void An_empty_whitespace_or_null_item_gives_the_syntax_problem_empty_with_the_path_of_the_item()
    {
        var result = Located.Compile(WithTrigger("close > 1", string.Empty, "   ", null!));

        result.ErrorList().ShouldBe(["syntax.empty@buy.trigger[1]", "syntax.empty@buy.trigger[2]", "syntax.empty@buy.trigger[3]"]);
    }

    [Fact]
    public void An_item_that_does_not_parse_keeps_the_syntax_problems_of_the_language_and_the_other_items_are_still_checked()
    {
        var result = Located.Compile(Basic().WithBuy(b => b with { Setup = Defs.All("rsi(14 >"), Trigger = Defs.All("closee > 1") }));

        result.ErrorList().ShouldBe(["syntax.unclosed_bracket@buy.setup[0]", "syntax.unexpected_end@buy.setup[0]", "lint.unknown_name@buy.trigger[0]"]);
    }

    [Fact]
    public void The_warnings_and_notes_of_an_item_are_listed_with_its_path_and_the_warnings_are_on_the_compiled_side()
    {
        var definition = Basic().WithBuy(b => b with { Trigger = Defs.All("tf(\"15m\").close > 1", "regime() == \"UP_LOW_VOL\"") });

        var result = Located.Compile(definition);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        result.ShouldHave(LintCodes.RedundantTimeframe, "buy.trigger[0]", ProblemSeverity.Warning);
        result.ShouldHave(LintCodes.NeedsContext, "buy.trigger[1]", ProblemSeverity.Information);
        result.Compiled!.Warnings.Select(w => w.Problem.Code).ShouldBe([LintCodes.RedundantTimeframe], "the note is a problem, not a warning");
    }

    [Fact]
    public void The_problems_of_an_item_equal_those_of_the_condition_compiler_with_the_path_added_for_500_seeded_items()
    {
        var parameters = ValidConditionGenerator.Parameters.Select(p => new StrategyParam(p.Key, p.Value)).ToList();
        var context = new CompileContext(Timeframe.M15, ValidConditionGenerator.Parameters);
        var generator = new ValidConditionGenerator(2026, Timeframe.M15);
        var random = new Random(77);
        var items = new List<string>();
        for (var i = 0; i < 500; i++)
        {
            var source = generator.Next().Source;
            items.Add(i % 2 == 0 ? source : Damage(source, random));
        }

        var invalid = 0;
        var withWarnings = 0;
        for (var batch = 0; batch < items.Count; batch += 5)
        {
            var texts = items.Skip(batch).Take(5).ToArray();
            var result = Located.Compile(Basic().WithBuy(b => b with { Trigger = Defs.All(texts) }) with { Params = parameters });
            for (var k = 0; k < texts.Length; k++)
            {
                var expected = ConditionCompiler.Compile(texts[k], context);
                var actual = result.Problems.Where(p => p.Path == "buy.trigger[" + k + "]").Select(p => p.Problem).ToList();

                // the strategy lists errors first and then the warnings and notes (criterion 27), each group in the order of the language: the same two sequences as the condition compiler gives, split by severity
                actual.Where(p => p.Severity == ProblemSeverity.Error).ShouldBe(expected.Problems.Where(p => p.Severity == ProblemSeverity.Error), "the errors of " + texts[k]);
                actual.Where(p => p.Severity != ProblemSeverity.Error).ShouldBe(expected.Problems.Where(p => p.Severity != ProblemSeverity.Error), "the warnings and notes of " + texts[k]);
                actual.Count.ShouldBe(expected.Problems.Count);
                invalid += expected.IsSuccess ? 0 : 1;
                withWarnings += expected.Problems.Any(p => p.Severity != ProblemSeverity.Error) ? 1 : 0;
            }
        }

        invalid.ShouldBeGreaterThan(100, "the corpus has to contain many invalid items");
        withWarnings.ShouldBeGreaterThan(10, "and some with warnings or notes");
    }

    private static string Damage(string source, Random random)
    {
        var at = random.Next(source.Length);
        return random.Next(3) switch
        {
            0 => source.Remove(at, 1),
            1 => source.Insert(at, "$"),
            _ => source[..at] + "x" + source[Math.Min(at + 1, source.Length)..],
        };
    }
}
