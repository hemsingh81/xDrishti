using System.Reflection;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>
/// ISS-12, ADR 0034 amendment 2: the warm-up compiles a fixed set of conditions so that the JIT has seen the compiler before the first real request. These tests are the guard the benchmarks cannot be (they need a quiet
/// machine and a fresh process): the sets are valid and invalid as they say, <c>Run</c> does what it says and is harmless to repeat, and the sets use every element of the language, so that a new builtin, operator, node,
/// catalog feature or kind of problem fails here until the warm-up covers it. No clock: a warm-up that misses a language feature costs the first request about 5 ms of JIT, which no test could assert.
/// </summary>
public sealed class CompilerWarmupTests
{
    /// <summary>The kinds of problem the warm-up leaves out, each with its reason; everything else in <see cref="SyntaxCodes"/> and <see cref="LintCodes"/> must be reported by some source.</summary>
    private static readonly IReadOnlyDictionary<string, string> LeftOut = new Dictionary<string, string>
    {
        [SyntaxCodes.TooLong] = "needs a text of more than 4000 characters; a guard for hostile input, a comparison and one message",
        [SyntaxCodes.TooDeep] = "needs a condition nested 65 levels deep; a guard for hostile input, one message",
        [SyntaxCodes.TooManyProblems] = "needs a text with more than 100 problems; a guard for hostile input, one message",
        [LintCodes.ShadowedName] = "needs a parameter named like a catalog feature, which would hide that feature from every other source of the set",
    };

    /// <summary>The bound nodes that never survive into a condition that compiled: the parts of a fault, the text of a comparison that is folded into a number or a minute, and the keyword that is folded into a comparison.</summary>
    private static readonly Type[] NeverInACompiledTree = [typeof(BoundInvalid), typeof(BoundText), typeof(BoundTimeKeyword)];

    private static readonly BinaryOperator[] Comparisons =
        [BinaryOperator.Less, BinaryOperator.LessOrEqual, BinaryOperator.Greater, BinaryOperator.GreaterOrEqual, BinaryOperator.Equal, BinaryOperator.NotEqual];

    private static readonly BinaryOperator[] Arithmetic = [BinaryOperator.Add, BinaryOperator.Subtract, BinaryOperator.Multiply, BinaryOperator.Divide];

    /// <summary>What a pass must report: every condition compiled and bound, every invalid one refused, and the same for the strategy definitions.</summary>
    private static CompilerWarmup.Outcome Expected() => new(
        CompilerWarmup.Valid.Count,
        CompilerWarmup.Valid.Count,
        CompilerWarmup.Invalid.Count,
        WarmupStrategies.Valid.Count,
        WarmupStrategies.Valid.Count,
        WarmupStrategies.Invalid.Count);

    /// <summary>The conditions of the valid set that compiled (a source that does not is reported by its own test).</summary>
    private static List<CompiledCondition> Compiled() => [.. CompilerWarmup.Valid.Select(c => Compile(c).Condition).OfType<CompiledCondition>()];

    private static CompileResult Compile(CompilerWarmup.Case item) => ConditionCompiler.Compile(item.Source, item.Context);

    private static List<SyntaxNode> SyntaxOf(IEnumerable<CompilerWarmup.Case> cases)
    {
        var nodes = new List<SyntaxNode>();
        foreach (var item in cases)
        {
            Collect(ConditionParser.Parse(item.Source).Tree!, nodes);
        }

        return nodes;
    }

    private static void Collect(SyntaxNode node, List<SyntaxNode> into)
    {
        into.Add(node);
        switch (node)
        {
            case Binary binary:
                Collect(binary.Left, into);
                Collect(binary.Right, into);
                break;
            case Unary unary:
                Collect(unary.Operand, into);
                break;
            case Subscript subscript:
                Collect(subscript.Target, into);
                break;
            case Call call:
                Collect(call.Name, into);
                foreach (var argument in call.Arguments)
                {
                    Collect(argument, into);
                }

                break;
            case PositionalArgument positional:
                Collect(positional.Value, into);
                break;
            case Reference reference:
                Collect(reference.Name, into);
                break;
        }
    }

    /// <summary>The kind of every node of the bound trees, with the operator or the direction where the node has one (<c>BoundCompare:Less</c>, <c>BoundCrossing:above</c>).</summary>
    private static HashSet<string> ShapesOf(IEnumerable<CompiledCondition> conditions)
    {
        var shapes = new HashSet<string>();
        foreach (var condition in conditions)
        {
            Collect(condition.Tree, shapes);
        }

        return shapes;
    }

    private static void Collect(IBoundExpression node, HashSet<string> into)
    {
        into.Add(node switch
        {
            BoundCompare compare => nameof(BoundCompare) + ":" + compare.Operator,
            BoundTimeCompare time => nameof(BoundTimeCompare) + ":" + time.Operator,
            BoundArithmetic arithmetic => nameof(BoundArithmetic) + ":" + arithmetic.Operator,
            BoundLogical logical => nameof(BoundLogical) + ":" + logical.Operator,
            BoundCrossing crossing => nameof(BoundCrossing) + (crossing.IsAbove ? ":above" : ":below"),
            BoundRun run => nameof(BoundRun) + (run.IsRising ? ":rising" : ":falling"),
            BoundMinMax minMax => nameof(BoundMinMax) + (minMax.IsMax ? ":max" : ":min"),
            _ => node.GetType().Name,
        });
        foreach (var child in BoundTree.Children(node))
        {
            Collect(child, into);
        }
    }

    [Fact]
    public void Every_source_of_the_valid_set_compiles_and_every_source_of_the_invalid_set_is_refused()
    {
        var notValid = CompilerWarmup.Valid.Where(c => !Compile(c).IsSuccess).Select(c => c.Source + " -> " + Compiling.Describe(Compile(c))).ToList();
        var notInvalid = CompilerWarmup.Invalid.Where(c => Compile(c).IsSuccess).Select(c => c.Source).ToList();

        notValid.ShouldBeEmpty("these sources of the valid set do not compile, so the warm-up would skip them");
        notInvalid.ShouldBeEmpty("these sources of the invalid set compile, so they warm up nothing of the error paths");
    }

    [Fact]
    public void Run_does_all_of_its_work_it_compiles_and_binds_every_source_of_the_valid_set_and_refuses_every_source_of_the_invalid_set()
    {
        var outcome = CompilerWarmup.Warm();

        outcome.ShouldBe(Expected());
    }

    [Fact]
    public void Run_may_be_called_again_and_from_several_threads_at_once_without_a_failure()
    {
        CompilerWarmup.Run();
        CompilerWarmup.Run();

        var threads = Enumerable.Range(0, 4).Select(_ => Task.Run(CompilerWarmup.Run, TestContext.Current.CancellationToken)).ToArray();

        Should.NotThrow(() => Task.WaitAll(threads, TestContext.Current.CancellationToken));
        CompilerWarmup.Warm().ShouldBe(Expected(), "the sets are fixed: another pass does the same work");
    }

    [Fact]
    public void The_sets_have_no_source_twice_and_share_none_with_the_benchmark_so_that_the_benchmark_measures_conditions_the_warm_up_has_not_seen()
    {
        var sources = CompilerWarmup.Valid.Concat(CompilerWarmup.Invalid).Select(c => c.Source).ToList();

        sources.ShouldBeUnique();
        sources.Intersect(TenConditions.Sources).ShouldBeEmpty();
    }

    [Fact]
    public void Every_builtin_every_binary_and_unary_operator_and_every_kind_of_syntax_node_is_used_by_a_valid_source()
    {
        var nodes = SyntaxOf(CompilerWarmup.Valid.Where(c => Compile(c).IsSuccess));
        var calls = nodes.OfType<Call>().Select(c => c.Name.Text).ToHashSet();

        Vocabulary.BuiltinNames.Except(calls).ShouldBeEmpty("builtins the valid set never calls");
        Enum.GetValues<BinaryOperator>().Except(nodes.OfType<Binary>().Select(b => b.Operator)).ShouldBeEmpty("binary operators the valid set never uses");
        Enum.GetValues<UnaryOperator>().Except(nodes.OfType<Unary>().Select(u => u.Operator)).ShouldBeEmpty("unary operators the valid set never uses");
        nodes.OfType<Subscript>().ShouldNotBeEmpty("a lagged read");
        nodes.OfType<NamedArgument>().ShouldNotBeEmpty("a named argument (source=)");
        nodes.OfType<Reference>().Any(r => r.Timeframe is not null).ShouldBeTrue("a timeframe qualifier on a reference");
        nodes.OfType<Call>().Any(c => c.Timeframe is not null).ShouldBeTrue("a timeframe qualifier on a call");
        nodes.OfType<NumberLiteral>().ShouldNotBeEmpty();
        nodes.OfType<StringLiteral>().ShouldNotBeEmpty();
        nodes.OfType<BoolLiteral>().ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_constant_every_dotted_name_and_every_strategy_parameter_is_used_by_a_valid_source()
    {
        var references = SyntaxOf(CompilerWarmup.Valid.Where(c => Compile(c).IsSuccess)).OfType<Reference>().ToList();

        Vocabulary.ConstantNames.Except(references.Select(r => r.Name.Text)).ShouldBeEmpty("constants the valid set never uses");
        CompilerWarmup.Parameters.Keys.Except(references.Select(r => r.Name.Text)).ShouldBeEmpty("strategy parameters the valid set never reads");
        var dotted = references.Where(r => r.Scope is not null).Select(r => r.Scope!.Text + "." + r.Name.Text).ToHashSet();
        var expected = Vocabulary.ScopeNames.SelectMany(scope => Vocabulary.TryScope(scope, out var members) ? members.Select(m => scope + "." + m) : []);
        expected.Except(dotted).ShouldBeEmpty("dotted spellings the valid set never uses");
    }

    [Fact]
    public void Every_kind_of_node_of_the_bound_tree_is_in_a_valid_condition_with_each_of_its_operators_and_directions()
    {
        var shapes = ShapesOf(Compiled());
        var expected = new List<string>
        {
            nameof(BoundNumber), nameof(BoundBool), nameof(BoundFeature), nameof(BoundNot), nameof(BoundNegate), nameof(BoundAbs), nameof(BoundBetween),
            nameof(BoundCrossing) + ":above", nameof(BoundCrossing) + ":below", nameof(BoundRun) + ":rising", nameof(BoundRun) + ":falling", nameof(BoundMinMax) + ":min", nameof(BoundMinMax) + ":max",
            nameof(BoundLogical) + ":And", nameof(BoundLogical) + ":Or",
        };
        expected.AddRange(Comparisons.Select(o => nameof(BoundCompare) + ":" + o));
        expected.AddRange(Comparisons.Select(o => nameof(BoundTimeCompare) + ":" + o));
        expected.AddRange(Arithmetic.Select(o => nameof(BoundArithmetic) + ":" + o));

        expected.Except(shapes).ShouldBeEmpty("bound nodes the valid set never produces");
        shapes.Except(expected).ShouldBeEmpty("bound nodes in a compiled tree that this test does not know; list them, with their operators, above");
    }

    [Fact]
    public void Every_bound_node_the_library_has_is_either_in_a_compiled_tree_or_one_that_never_is()
    {
        var implementations = typeof(IBoundExpression).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IBoundExpression).IsAssignableFrom(t)).Select(t => t.Name).ToList();
        var shapes = ShapesOf(Compiled()).Select(s => s.Split(':')[0]).ToHashSet();

        implementations.ShouldNotBeEmpty();
        implementations.Except(shapes).Except(NeverInACompiledTree.Select(t => t.Name)).ShouldBeEmpty("a new bound node: add a source that produces it to the warm-up, or say here why none can");
        shapes.Intersect(NeverInACompiledTree.Select(t => t.Name)).ShouldBeEmpty("a node listed as never in a compiled tree is in one: take it off the list and cover it");
    }

    [Fact]
    public void Every_feature_of_the_catalog_is_read_by_a_valid_source()
    {
        var read = Compiled().SelectMany(c => c.Features).Select(f => f.Name).ToHashSet();

        IndicatorCatalog.All.Select(i => i.Name).Except(read).ShouldBeEmpty("catalog features the valid set never reads");
    }

    [Fact]
    public void Every_timeframe_is_the_strategy_or_a_feature_of_a_valid_source()
    {
        var used = Compiled().SelectMany(c => c.Features.Select(f => f.Timeframe).Append(c.EvaluationTimeframe)).ToHashSet();

        Enum.GetValues<Timeframe>().Except(used).ShouldBeEmpty("timeframes the valid set never uses");
        CompilerWarmup.Valid.Select(c => c.Timeframe).ShouldContain(Timeframe.D1, "a daily strategy");
    }

    [Fact]
    public void The_valid_set_reports_every_warning_and_every_note_and_the_two_sets_together_every_error_the_library_has()
    {
        var valid = CompilerWarmup.Valid.SelectMany(c => Compile(c).Problems).Select(p => p.Code).ToHashSet();
        var all = valid.Concat(CompilerWarmup.Invalid.SelectMany(c => Compile(c).Problems).Select(p => p.Code)).ToHashSet();
        var codes = new[] { typeof(SyntaxCodes), typeof(LintCodes) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        new[] { LintCodes.ConstantCondition, LintCodes.NeverTrue, LintCodes.RedundantTimeframe, LintCodes.LagAcrossSession, LintCodes.NeedsContext, LintCodes.NeedsExpiryDays }.ShouldBeSubsetOf(valid, "warnings and notes of a condition that compiles");
        codes.Except(all).Except(LeftOut.Keys).ShouldBeEmpty("kinds of problem that no source of the warm-up reports");
        all.Intersect(LeftOut.Keys).ShouldBeEmpty("a kind of problem listed as left out is reported by the warm-up: take it off the list");
    }
}
