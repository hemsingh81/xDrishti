using System.Linq.Expressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>
/// The cost model of <c>rising(x, n)</c> and <c>falling(x, n)</c> (ADR 0034, Amendment 18), pinned without a clock: a run is <b>one loop of exactly n steps</b> that reads the operand once per step (one read for each
/// feature in it) and has <b>no early exit</b>, so its work is n × reads whatever the data, and the tree does not grow with n (nothing is unrolled, nothing is nested, so nothing is quadratic). The expression tree of the
/// condition is counted, so the test is exact and runs in microseconds; the explicit benchmark <c>Evaluate_runs_by_count</c> measures what it costs. A gate on the shape of the loop catches a body that reads both the newer and the
/// older value, an unrolled or nested loop, and a loop that runs to another count than the one written.
/// </summary>
public sealed class RunCostTests
{
    private static Expression<Decision> LambdaOf(string source)
    {
        var condition = Compiling.Condition(source, Timeframe.M15);
        return ExpressionBuilder.Lambda(condition.Tree, condition.Features);
    }

    /// <summary>Counts the nodes of a tree, collects its loops and counts the reads of the frame inside them.</summary>
    private sealed class Census : ExpressionVisitor
    {
        public int Nodes { get; private set; }

        public List<LoopExpression> Loops { get; } = [];

        public override Expression? Visit(Expression? node)
        {
            Nodes += node is null ? 0 : 1;
            return base.Visit(node);
        }

        protected override Expression VisitLoop(LoopExpression node)
        {
            Loops.Add(node);
            return base.VisitLoop(node);
        }

        public static Census Of(Expression tree)
        {
            var census = new Census();
            census.Visit(tree);
            return census;
        }

    }

    /// <summary>The calls that read a feature of the frame (<c>Kernel.Value</c> and <c>Kernel.ValueAt</c>) in a tree.</summary>
    private sealed class ReadCounter : ExpressionVisitor
    {
        public int Count { get; private set; }

        public static int In(Expression tree)
        {
            var counter = new ReadCounter();
            counter.Visit(tree);
            return counter.Count;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Count += node.Method == KernelMethods.Value || node.Method == KernelMethods.ValueAt ? 1 : 0;
            return base.VisitMethodCall(node);
        }
    }

    /// <summary>The count a loop runs to: the constant its exit test compares the counter with (the one <c>&gt;</c> in the body of a run).</summary>
    private static int ExitCount(LoopExpression loop)
    {
        var exits = new List<BinaryExpression>();
        new ExitFinder(exits).Visit(loop.Body);

        var exit = exits.ShouldHaveSingleItem("the body of a run has one comparison, the exit test");
        return exit.Right.ShouldBeOfType<ConstantExpression>().Value.ShouldBeOfType<int>();
    }

    private sealed class ExitFinder(List<BinaryExpression> found) : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.GreaterThan)
            {
                found.Add(node);
            }

            return base.VisitBinary(node);
        }
    }

    private static void ShouldBeOneLoopOfNStepsReadingOncePerLeaf(string template, int[] counts, int leaves)
    {
        var nodes = new List<int>();
        foreach (var n in counts)
        {
            var source = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, n);

            var census = Census.Of(LambdaOf(source));

            var loop = census.Loops.ShouldHaveSingleItem(source);
            ExitCount(loop).ShouldBe(n, source);
            ReadCounter.In(loop.Body).ShouldBe(leaves, source + ": one read of the operand's feature for each leaf of it, per step, and none for the newer value");
            nodes.Add(census.Nodes);
        }

        nodes.Distinct().ShouldHaveSingleItem("the tree has the same size for every count: " + string.Join(", ", counts.Zip(nodes, (n, size) => $"n={n}: {size} nodes")));
    }

    [Fact]
    public void A_run_over_a_plain_feature_is_one_loop_of_n_steps_with_one_read_per_step_and_a_tree_that_does_not_grow_with_n()
    {
        ShouldBeOneLoopOfNStepsReadingOncePerLeaf("rising(close, {0})", [1, 3, 50, 254, 255], leaves: 1);
        ShouldBeOneLoopOfNStepsReadingOncePerLeaf("falling(close, {0})", [1, 3, 50, 254, 255], leaves: 1);
    }

    [Fact]
    public void A_run_over_an_operand_of_two_reads_is_one_loop_of_n_steps_with_two_reads_per_step_and_a_tree_that_does_not_grow_with_n()
    {
        // close[3] + open reads two features, one of them three bars back: n goes to 252 (3 + 252 = 255 is the deepest lag the frame keeps)
        ShouldBeOneLoopOfNStepsReadingOncePerLeaf("falling(close[3] + open, {0})", [1, 3, 50, 252], leaves: 2);
        ShouldBeOneLoopOfNStepsReadingOncePerLeaf("rising(close + close, {0})", [1, 3, 50, 254, 255], leaves: 2);
    }

    [Fact]
    public void Two_runs_in_one_condition_are_two_loops_and_a_run_next_to_comparisons_adds_no_loop()
    {
        Census.Of(LambdaOf("rising(close, 3) and falling(open, 4)")).Loops.Count.ShouldBe(2);
        Census.Of(LambdaOf("rising(close, 3) and close > open and crosses_above(close, vwap)")).Loops.Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void Only_a_condition_with_a_run_has_a_loop_and_it_has_one_for_each_run(Timeframe evaluation)
    {
        var audited = 0;
        foreach (var source in CompiledTreeTests.Written)
        {
            var result = Compiling.Of(source, evaluation);
            if (result.Condition is null)
            {
                continue; // what the timeframe itself refuses (the time on a day, vwap on a day …)
            }

            var runs = RunNames.Sum(name => CountOf(source, name));

            Census.Of(ExpressionBuilder.Lambda(result.Condition.Tree, result.Condition.Features)).Loops.Count.ShouldBe(runs, source + " on " + evaluation);
            audited++;
        }

        audited.ShouldBeGreaterThan(CompiledTreeTests.Written.Length / 2, evaluation + " refuses too many of the written conditions to say anything about them");
    }

    public static TheoryData<Timeframe> Evaluations { get; } = [.. Timeframes.All];

    private static readonly string[] RunNames = ["rising(", "falling("];

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
