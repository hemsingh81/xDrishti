using System.Linq.Expressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The mutant control of the oracle (ADR 0034, Amendment 14). The look-ahead property cannot fail for a wrong <i>compiler</i>: a compiled condition is a pure function of the frame as it is at the step, and the frame has no
/// door to the future. A compiler that reads the wrong lag (a feature one bar nearer than written, the previous value taken as the current one, a run that forgets the lag written inside it) or the wrong clock (<c>time</c>
/// one minute ahead, or at the start of the bar) is caught by the interpreter oracle and the hand examples, and the audit of the expression trees (<c>CompiledTreeTests</c>) cannot see it: it proves no negative lag, an additive
/// shape and kernel-only access, not that the lag is the one written. This control shows that the oracle <b>can</b> fail: it takes the real expression tree of every condition of the corpus, changes it the way a faulty
/// compiler would, compiles the changed tree, and runs it through the same blocks and the same comparison (<see cref="Tally.Compare"/>) as
/// <see cref="The_compiled_condition_equals_the_interpreter_at_every_step_on_random_frames"/>, stopping at the first difference. The whole corpus is walked, not a sample: the mutant that drops the written lag of a run
/// is first noticed in the ninth block (1m, without a context). What each layer sees: the truncation property, the feed and the frame; the audit, the shape of the tree; the oracle corpus, the hand examples and the kernel
/// tests, the lag and the clock.
/// </summary>
public sealed partial class InterpreterOracleTests
{
    private const string LagOneNearer = "a feature is read one bar nearer than written";

    private const string PreviousIsCurrent = "the previous value is the current value";

    private const string RunDropsWrittenLag = "a run forgets the lag written in its operand";

    private const string ClockOneMinuteAhead = "the clock is one minute ahead";

    private const string ClockAtBarStart = "the clock is at the start of the bar";

    private const string Unchanged = "nothing is changed";

    /// <summary>What a walk over the corpus found: the blocks it walked, the comparisons it made and the mismatches it counted (it stops at the first when asked to).</summary>
    private sealed record Walked(int Blocks, int Comparisons, int Mismatches);

    [Theory]
    [InlineData(LagOneNearer)]
    [InlineData(PreviousIsCurrent)]
    [InlineData(RunDropsWrittenLag)]
    [InlineData(ClockOneMinuteAhead)]
    [InlineData(ClockAtBarStart)]
    public void A_compiler_that_reads_the_wrong_lag_or_the_wrong_clock_is_noticed_by_the_oracle_corpus(string mutant)
    {
        var walked = Walk(mutant, Timeframes.All, int.MaxValue, stopAtTheFirstMismatch: true);

        walked.Mismatches.ShouldBeGreaterThan(0, $"the oracle corpus no longer notices a compiler where {mutant} ({walked.Blocks} blocks, {walked.Comparisons} comparisons walked without a difference)");
    }

    [Fact]
    public void The_mutant_control_changes_nothing_when_nothing_is_changed_so_that_a_difference_is_the_mutants()
    {
        var walked = Walk(Unchanged, [Timeframe.M15], 2, stopAtTheFirstMismatch: false);

        walked.Mismatches.ShouldBe(0, "the control is not clean: the wiring of the mutant control itself differs from the interpreter");
        walked.Blocks.ShouldBe(2);
        walked.Comparisons.ShouldBeGreaterThan(1_000);
    }

    /// <summary>Walks the blocks of the corpus in the order of the main test (the evaluation timeframes, both flavours, six blocks), with the condition of every block changed the way <paramref name="mutant"/> says.</summary>
    private static Walked Walk(string mutant, IReadOnlyList<Timeframe> evaluations, int maxBlocks, bool stopAtTheFirstMismatch)
    {
        var rejected = new List<string>();
        var tally = new Tally();
        var blocks = 0;
        foreach (var evaluation in evaluations)
        {
            for (var flavour = 0; flavour < Flavours; flavour++)
            {
                for (var block = 0; block < Blocks; block++)
                {
                    if (blocks == maxBlocks)
                    {
                        return new Walked(blocks, tally.Comparisons, tally.Mismatches.Count);
                    }

                    var run = Prepare(SeedOf(evaluation, flavour, block), evaluation, withContext: flavour == 0, block, rejected);
                    rejected.ShouldBeEmpty("the compiler refused conditions of the language:\n" + string.Join("\n", rejected));
                    blocks++;
                    var (frame, _) = run.NewFrame();
                    var changed = run.Compiled.Select(condition => Changed(condition, frame, mutant)).ToList();
                    var sessions = SessionReference.For(frame);
                    var interpreter = new Interpreter(frame, sessions);
                    var boundTreeInterpreter = new BoundTreeInterpreter(frame, sessions);
                    tally.Compare(run, frame, interpreter, boundTreeInterpreter, changed);
                    foreach (var bar in run.Bars)
                    {
                        if (stopAtTheFirstMismatch && tally.Mismatches.Count > 0)
                        {
                            return new Walked(blocks, tally.Comparisons, tally.Mismatches.Count);
                        }

                        frame.Step(bar);
                        sessions.Feed(bar);
                        tally.Compare(run, frame, interpreter, boundTreeInterpreter, changed);
                    }

                    if (stopAtTheFirstMismatch && tally.Mismatches.Count > 0)
                    {
                        return new Walked(blocks, tally.Comparisons, tally.Mismatches.Count);
                    }
                }
            }
        }

        return new Walked(blocks, tally.Comparisons, tally.Mismatches.Count);
    }

    /// <summary>The condition with its expression tree changed by <paramref name="mutant"/>, compiled and bound to the handles of the frame, as the compiler binds an honest one.</summary>
    private static BoundCondition Changed(CompiledCondition condition, FeatureFrame frame, string mutant)
    {
        var lambda = ExpressionBuilder.Lambda(condition.Tree, condition.Features);
        var changed = (Expression<Decision>)new Mutation(mutant).Visit(lambda);
        int[] handles = [.. condition.Features.Select(feature => frame.HandleOf(feature))];
        return new BoundCondition(condition, changed.Compile(), handles);
    }

    /// <summary>Rewrites the expression tree of a condition the way a faulty compiler would have built it. Only the calls that read the frame at a lag, and the clock, are touched.</summary>
    private sealed class Mutation(string mutant) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method == KernelMethods.CloseMinute && mutant is ClockOneMinuteAhead or ClockAtBarStart)
            {
                return Expression.Add(node, Expression.Constant(mutant == ClockOneMinuteAhead ? 1.0 : -1.0));
            }

            if (node.Method != KernelMethods.ValueAt)
            {
                return base.VisitMethodCall(node);
            }

            var (frame, handle, lag) = (node.Arguments[0], node.Arguments[1], node.Arguments[2]);
            return mutant switch
            {
                LagOneNearer => Expression.Call(node.Method, frame, handle, OneNearer(lag)),
                PreviousIsCurrent when lag is ConstantExpression { Value: 1 } => Expression.Call(KernelMethods.Value, frame, handle),
                RunDropsWrittenLag when lag is BinaryExpression { NodeType: ExpressionType.Add, Left: ConstantExpression { Value: int }, Right: ParameterExpression counter } => Expression.Call(node.Method, frame, handle, counter),
                _ => base.VisitMethodCall(node),
            };
        }

        /// <summary>A written lag of at least 1 becomes one less, in a constant and in the constant part of a run's <c>written + counter</c>.</summary>
        private static Expression OneNearer(Expression lag) => lag switch
        {
            ConstantExpression { Value: int n } when n > 0 => Expression.Constant(n - 1),
            BinaryExpression { NodeType: ExpressionType.Add, Left: ConstantExpression { Value: int n }, Right: var counter } when n > 0 => Expression.Add(Expression.Constant(n - 1), counter),
            _ => lag,
        };
    }
}
