using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Criterion 27: the problems come in the order of the model, errors first, capped at 100 and 100, and a mutated definition never throws.</summary>
public sealed class ProblemOrderAndFuzzTests
{
    [Fact]
    public void Problems_come_in_the_order_of_the_model_schema_id_style_timeframes_sides_params_the_sections_then_time_rules_and_filters()
    {
        var definition = Defs.Basic().WithBuy(b => b with
        {
            Setup = Defs.All("closee > 1"),
            Trigger = Defs.All("opn > 1"),
            Invalidate = Defs.All("hig > 1"),
            Entry = null,
            Stop = new StopSpec(StopKind.SwingLow),
            Exits = [],
            Overlays = [new OverlaySpec(OverlayKind.TimeStop, [Defs.P("Bad", 1)])],
        }) with
        {
            Schema = 3,
            Id = "9",
            Style = (Style)4,
            Timeframes = [Timeframe.M15, Timeframe.M15],
            Sides = [Side.Buy, Side.Buy],
            Params = [new StrategyParam("a", 1), new StrategyParam("a", 2)],
            TimeRules = new TimeRules(new TimeOnly(9, 14), null, null, 2),
            Filters = ["zzz"],
        };

        var result = Located.Compile(definition);

        result.ErrorList().ShouldBe(
        [
            "model.schema_unsupported@schema",
            "model.bad_id@id",
            "model.undefined_value@style",
            "model.duplicate_timeframe@timeframes[1]",
            "model.duplicate_side@sides[1]",
            "model.duplicate_param@params[1]",
            "lint.unknown_name@buy.setup[0]",
            "lint.unknown_name@buy.trigger[0]",
            "lint.unknown_name@buy.invalidate[0]",
            "model.entry_missing@buy.entry",
            "model.field_missing@buy.stop.lookback",
            "model.exits_empty@buy.exits",
            "model.field_shape@buy.overlays[0]",
            "model.field_missing@buy.overlays[0].min_progress_rr",
            "model.bad_time@time_rules.first_entry",
            "model.unknown_filter@filters[0]",
        ]);
    }

    [Fact]
    public void Errors_come_first_and_the_warnings_and_notes_after_them_each_group_in_its_order()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All("tf(\"15m\").close > 1"), Trigger = Defs.All("closee > 1", "regime() == \"UP_LOW_VOL\"") }) with
        {
            Params = [new StrategyParam("close", 1)],
            Filters = [FilterNames.NotEventDay],
        };

        var result = Located.Compile(definition);

        result.Problems.Select(p => p.Problem.Severity).ShouldBe([ProblemSeverity.Error, ProblemSeverity.Warning, ProblemSeverity.Warning, ProblemSeverity.Information, ProblemSeverity.Warning]);
        result.Problems.Select(p => p.Path).ShouldBe(["buy.trigger[0]", "params[0]", "buy.setup[0]", "buy.trigger[1]", "filters[0]"]);
    }

    [Fact]
    public void A_hundred_and_one_errors_give_the_first_100_and_then_one_too_many_problems_with_severity_error_and_an_empty_path()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All([.. Enumerable.Repeat("closee > 1", 100)]), Trigger = Defs.All("rsi(14) > 55 and opn > 1") });

        var result = Located.Compile(definition);

        result.Problems.Count.ShouldBe(101);
        result.IsSuccess.ShouldBeFalse();
        result.Problems.Take(100).ShouldAllBe(p => p.Problem.Code == LintCodes.UnknownName && p.Path.StartsWith("buy.setup[", StringComparison.Ordinal), "the first 100 are the setup items, in order; the 101st (the trigger) was dropped");
        result.Problems[99].Path.ShouldBe("buy.setup[99]");
        var last = result.Problems[100];
        (last.Path, last.Problem.Code, last.Problem.Severity, last.Problem.Span).ShouldBe((string.Empty, ModelCodes.TooManyProblems, ProblemSeverity.Error, new SourceSpan(0, 0, 1, 1)));
    }

    [Fact]
    public void A_hundred_warnings_compile_and_a_hundred_and_one_give_100_and_one_too_many_problems_with_severity_information()
    {
        var hundred = Defs.Basic().WithBuy(b => b with { Setup = Defs.All([.. Enumerable.Repeat("tf(\"15m\").close > 1", 99)]), Trigger = Defs.All("tf(\"15m\").close > 1") });
        var more = Defs.Basic().WithBuy(b => b with { Setup = Defs.All([.. Enumerable.Repeat("tf(\"15m\").close > 1", 100)]), Trigger = Defs.All("tf(\"15m\").close > 1") });

        var exactly = Located.Compile(hundred);
        var over = Located.Compile(more);

        exactly.IsSuccess.ShouldBeTrue();
        exactly.Problems.Count.ShouldBe(100);
        exactly.Problems.ShouldAllBe(p => p.Problem.Code == LintCodes.RedundantTimeframe);
        over.IsSuccess.ShouldBeTrue("the compile fails on errors only");
        over.Problems.Count.ShouldBe(101);
        over.Problems.Take(100).ShouldAllBe(p => p.Problem.Code == LintCodes.RedundantTimeframe && p.Problem.Severity == ProblemSeverity.Warning);
        over.Problems[100].Problem.Code.ShouldBe(ModelCodes.TooManyProblems);
        over.Problems[100].Problem.Severity.ShouldBe(ProblemSeverity.Information);
        over.Problems[100].Path.ShouldBeEmpty();
        over.Compiled!.Warnings.Count.ShouldBe(100, "the compiled side keeps the warnings that were listed");
    }

    [Fact]
    public void Both_caps_can_apply_at_once_and_the_errors_are_listed_before_the_warnings()
    {
        var definition = Defs.Basic().WithBuy(b => b with { Setup = Defs.All([.. Enumerable.Repeat("closee > 1", 100)]), Trigger = Defs.All([.. Enumerable.Repeat("tf(\"15m\").close > 1", 100)]), Invalidate = Defs.All("opn > 1") }) with { Filters = [FilterNames.NotEventDay] };

        var result = Located.Compile(definition);

        result.Problems.Count.ShouldBe(100 + 1 + 100 + 1);
        result.Problems.Count(p => p.Problem.Code == ModelCodes.TooManyProblems).ShouldBe(2);
        result.Problems[100].Problem.Severity.ShouldBe(ProblemSeverity.Error);
        result.Problems[201].Problem.Severity.ShouldBe(ProblemSeverity.Information);
    }

    // ---- the fuzz ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly Func<StrategyDefinition>[] Bases = [Defs.VwapRsiTrend, () => Defs.Orb(), () => Defs.Basic(), () => Defs.Swing()];

    private static readonly Timeframe[] Entry = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1];

    private static readonly string[] ParameterNames = ["rr", "x0", "x1", "t2_rr", "mult", "activate_at_rr", "lookback_bars", "length", "add_at_rr", "add_pct", "max_adds", "initial_pct", "intraday_bars", "swing_sessions", "days_before_results", "min_progress_rr", "enabled", "trail"];

    private static IReadOnlyList<ExitParameter> Parameters(Random random) =>
        [.. Enumerable.Range(0, random.Next(0, 4)).Select(i => new ExitParameter(random.Next(5) == 0 ? "Bad" + i : ParameterNames[random.Next(ParameterNames.Length)], [.. Enumerable.Range(0, random.Next(0, 13)).Select(_ => RandomQuantity(random))]))];

    private static Quantity RandomQuantity(Random random) => random.Next(8) switch
    {
        0 => new Quantity(null, null),
        1 => new Quantity(1, "aa"),
        2 => Quantity.Of(double.NaN),
        3 => Quantity.Named("zzz"),
        4 => Quantity.Of(double.NegativeInfinity),
        5 => Quantity.Of(random.Next(-5, 100)),
        _ => Quantity.Of(random.NextDouble() * 12),
    };

    private static Quantity? MaybeQuantity(Random random) => random.Next(2) == 0 ? null : RandomQuantity(random);

    private static ConditionList? MutatedList(ConditionList? list, Random random)
    {
        switch (random.Next(10))
        {
            case 0: return null;
            case 1: return new ConditionList((ListMode)random.Next(-2, 6), list?.Items ?? ["close > 1"]);
            case 2: return new ConditionList(ListMode.All, []);
            case 3: return new ConditionList(ListMode.Any, null!);
            case 4: return new ConditionList(ListMode.All, [.. Enumerable.Repeat("close > 1", 101)]);
            case 5: return new ConditionList(ListMode.All, [string.Empty, "   ", null!, new string('a', 5_000), "close > 1"]);
            case 6: return new ConditionList(ListMode.All, [new string('(', 5_000)]);
            case 7: return new ConditionList(ListMode.Any, [.. (list?.Items ?? ["close > 1"]).Select(i => Damage(i, random))]);
            default: return list;
        }
    }

    private static string Damage(string text, Random random)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var at = random.Next(text.Length);
        return random.Next(3) switch
        {
            0 => text.Remove(at, 1),
            1 => text.Insert(at, "$("),
            _ => text[..at] + "\"" + text[(at + 1)..],
        };
    }

    private static StrategyDefinition OnBuy(StrategyDefinition definition, Func<SideRules, SideRules> change) => definition with { Buy = definition.Buy is null ? null : change(definition.Buy) };

    private static StrategyDefinition OnSell(StrategyDefinition definition, Func<SideRules, SideRules> change) => definition with { Sell = definition.Sell is null ? null : change(definition.Sell) };

    private static StrategyDefinition Mutate(StrategyDefinition definition, Random random)
    {
        switch (random.Next(24))
        {
            case 0: return definition with { Id = random.Next(2) == 0 ? null! : new string('x', 70) };
            case 1: return definition with { Style = (Style)random.Next(-3, 9) };
            case 2: return definition with { Timeframes = random.Next(2) == 0 ? null! : [(Timeframe)random.Next(-2, 14), Timeframe.M15] };
            case 3: return definition with { Sides = random.Next(2) == 0 ? null! : [(Side)random.Next(-2, 5), Side.Buy] };
            case 4: return definition with { Params = random.Next(3) == 0 ? null! : [.. definition.Params, new StrategyParam(random.Next(2) == 0 ? null! : "q" + random.Next(9), double.NaN), null!] };
            case 5: return definition with { Filters = random.Next(2) == 0 ? null! : [null!, "x", "not_event_day"] };
            case 6: return definition with { TimeRules = random.Next(3) == 0 ? null : new TimeRules(new TimeOnly(random.Next(0, 24), random.Next(0, 60), random.Next(0, 60)), new TimeOnly(random.Next(0, 24), random.Next(0, 60)), new TimeOnly(random.Next(0, 24), random.Next(0, 60)), random.Next(-3, 9)) };
            case 7: return definition with { Buy = null };
            case 8: return definition with { Sell = null };
            case 9: return OnBuy(definition, b => b with { Setup = MutatedList(b.Setup, random) });
            case 10: return OnBuy(definition, b => b with { Trigger = MutatedList(b.Trigger, random) });
            case 11: return OnBuy(definition, b => b with { Invalidate = MutatedList(b.Invalidate, random) });
            case 12: return OnSell(definition, s => s with { Trigger = MutatedList(s.Trigger, random) });
            case 13: return OnBuy(definition, b => b with { Entry = random.Next(2) == 0 ? null : new EntrySpec((EntryKind)random.Next(-2, 6)) });
            case 14:
                return OnBuy(definition, b => b with
                {
                    Stop = random.Next(3) == 0 ? null : new StopSpec((StopKind)random.Next(-2, 10), random.Next(2) == 0 ? null : (Timeframe)random.Next(-2, 14), MaybeQuantity(random), MaybeQuantity(random), MaybeQuantity(random), MaybeQuantity(random), MaybeQuantity(random), MaybeQuantity(random)),
                });
            case 15: return OnSell(definition, s => s with { Stop = new StopSpec((StopKind)random.Next(0, 7), Minutes: RandomQuantity(random), Lookback: RandomQuantity(random), BufferAtr: RandomQuantity(random)) });
            case 16:
                return OnBuy(definition, b => b with
                {
                    Exits = random.Next(3) == 0 ? null : [.. Enumerable.Range(0, random.Next(0, 5)).Select(_ => random.Next(8) == 0 ? null! : new ExitSpec((ExitPolicy)random.Next(-1, 9), random.Next(6) == 0 ? null! : Parameters(random)))],
                });
            case 17:
                return OnBuy(definition, b => b with
                {
                    Overlays = random.Next(3) == 0 ? null : [.. Enumerable.Range(0, random.Next(0, 4)).Select(_ => random.Next(6) == 0 ? null! : new OverlaySpec((OverlayKind)random.Next(-1, 4), Parameters(random)))],
                });
            case 18: return OnBuy(definition, b => b with { Trigger = new ConditionList(ListMode.All, [string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", random.Next(1, 120)))]) });
            case 19: return definition with { Schema = random.Next(-2, 4) };
            case 20: return OnBuy(definition, b => b with { Trigger = MutatedList(b.Trigger, random), Setup = MutatedList(b.Setup, random), Stop = null });
            case 21: return definition with { Params = [new StrategyParam("close", 1), new StrategyParam("rsi", 2), new StrategyParam("time", 3), new StrategyParam("level", double.PositiveInfinity)] };
            case 22: return OnBuy(definition, b => b with { Exits = [new ExitSpec(ExitPolicy.PartialTrail, [new ExitParameter("atr_period", [Quantity.Of(random.Next(-3, 20_000) / 3.0)]), new ExitParameter(null!, null!), null!])] });
            default: return definition with { Id = definition.Id + random.Next(3) };
        }
    }

    private static int Rank(string path) => path switch
    {
        _ when path.StartsWith("time_rules", StringComparison.Ordinal) => 3,
        _ when path.StartsWith("filters", StringComparison.Ordinal) => 4,
        _ when path.StartsWith("buy", StringComparison.Ordinal) || path.StartsWith("sell", StringComparison.Ordinal) => 2,
        _ => 1,
    };

    [Fact]
    public void For_200_seeded_definitions_with_FIXED_RR_exits_only_the_exit_tables_add_no_problem_and_the_groups_keep_their_order_the_side_then_the_time_rules_then_the_filters()
    {
        var checkedDefinitions = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(10_000 + seed);
            var original = new Func<StrategyDefinition>[] { () => Defs.Orb(), () => Defs.Basic(), () => Defs.Swing() }[seed % 3]();
            var definition = original;
            for (var m = random.Next(1, 4); m > 0; m--)
            {
                var mutated = Mutate(definition, random);
                if (ReferenceEquals(mutated.Buy?.Exits, original.Buy?.Exits) && ReferenceEquals(mutated.Buy?.Overlays, original.Buy?.Overlays) && ReferenceEquals(mutated.Sell?.Exits, original.Sell?.Exits) && ReferenceEquals(mutated.Sell?.Overlays, original.Sell?.Overlays))
                {
                    definition = mutated;
                }
            }

            var side = seed % 4 == 3 ? Side.Sell : Side.Buy;
            var result = StrategyCompiler.Compile(definition, Entry[random.Next(Entry.Length)], side);

            result.Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.ExitNotEvaluated, "seed " + seed);
            result.Problems.Where(p => p.Path.Contains(".exits", StringComparison.Ordinal) || p.Path.Contains(".overlays", StringComparison.Ordinal)).ShouldAllBe(p => p.Problem.Code == ModelCodes.ExitsEmpty, "FIXED_RR-only definitions have no exit problem (seed " + seed + ")");
            var errorRanks = result.Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error && p.Path.Length > 0).Select(p => Rank(p.Path)).ToList();
            errorRanks.ShouldBe([.. errorRanks.Order()], "the problems come in the order of the model (seed " + seed + ")");
            checkedDefinitions++;
        }

        checkedDefinitions.ShouldBe(200);
    }

    [Fact]
    public void A_thousand_seeded_mutations_of_the_example_definitions_never_throw_and_every_problem_has_a_code_a_path_and_for_the_language_a_span_and_two_compiles_agree()
    {
        var compiled = 0;
        var refused = 0;
        var languageProblems = 0;
        var codes = new HashSet<string>();
        for (var seed = 0; seed < 1_000; seed++)
        {
            var random = new Random(seed);
            var definition = Bases[seed % Bases.Length]();
            for (var m = random.Next(1, 4); m > 0; m--)
            {
                definition = Mutate(definition, random);
            }

            var evaluation = Entry[random.Next(Entry.Length)];
            var side = random.Next(3) == 0 ? Side.Sell : Side.Buy;

            var first = StrategyCompiler.Compile(definition, evaluation, side);
            var second = StrategyCompiler.Compile(definition, evaluation, side);

            second.Problems.ShouldBe(first.Problems, "two compiles of one definition give equal problem lists (seed " + seed + ")");
            first.IsSuccess.ShouldBe(!first.Problems.Any(p => p.Problem.Severity == ProblemSeverity.Error));
            foreach (var located in first.Problems)
            {
                located.Problem.Code.StartsWith("strategy.", StringComparison.Ordinal).ShouldBeTrue("seed " + seed);
                (located.Path.Length > 0 || located.Problem.Code == ModelCodes.TooManyProblems).ShouldBeTrue("a problem has a path: " + located.Problem.Code + " (seed " + seed + ")");
                located.Problem.Span.Line.ShouldBeGreaterThanOrEqualTo(1);
                located.Problem.Span.Column.ShouldBeGreaterThanOrEqualTo(1);
                (located.Problem.Span.End >= located.Problem.Span.Start).ShouldBeTrue();
                if (!located.Problem.Code.StartsWith(ModelCodes.TooManyProblems[..15], StringComparison.Ordinal))
                {
                    languageProblems++;
                    located.Path.Contains('[', StringComparison.Ordinal).ShouldBeTrue("a problem of the language is in an item: " + located.Problem.Code + " at " + located.Path);
                }

                codes.Add(located.Problem.Code);
            }

            first.Problems.Count(p => p.Problem.Severity == ProblemSeverity.Error).ShouldBeLessThanOrEqualTo(101);
            compiled += first.IsSuccess ? 1 : 0;
            refused += first.IsSuccess ? 0 : 1;
        }

        compiled.ShouldBeGreaterThan(20, "some mutations leave a valid definition");
        refused.ShouldBeGreaterThan(500, "most mutations break it");
        languageProblems.ShouldBeGreaterThan(100);
        codes.Count(c => c.StartsWith("strategy.model.", StringComparison.Ordinal)).ShouldBeGreaterThan(15, "the fuzz reaches many codes of the model");
    }
}
