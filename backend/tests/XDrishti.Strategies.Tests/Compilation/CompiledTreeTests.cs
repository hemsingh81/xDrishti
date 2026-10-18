using System.Linq.Expressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>
/// The structural guarantee behind "a compiled condition cannot look ahead", tested on the expression trees themselves: the truncation property (<see cref="Evaluation.LookAheadPropertyTests"/>) shows that the feed of the
/// frame is honest, but it cannot fail for a wrong compiler, because a compiled condition is a pure function of the frame as it is at the step. What keeps the compiler from reading the future is that a tree can only call the
/// kernel, only reach the frame through the three doors (value, value some bars ago, clock), hold nothing but numbers, and read no negative lag (<see cref="ExpressionAudit"/>). That a lag is <i>exactly</i> the one written
/// is not what the audit proves: it is held by the interpreter oracle and the hand examples (the mutant control in <see cref="Evaluation.InterpreterOracleTests"/> shows they notice a lag or a clock that is wrong).
/// Checked on hand-written conditions that use every construct and on thousands of generated ones, with negative controls that show each rule can fail.
/// </summary>
public sealed class CompiledTreeTests
{
    private static AuditReport Audit(string source, Timeframe timeframe, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var condition = Compiling.Condition(source, timeframe, parameters);
        return ExpressionAudit.Of(ExpressionBuilder.Lambda(condition.Tree, condition.Features));
    }

    public static TheoryData<Timeframe> Evaluations { get; } = [.. Timeframes.All];

    /// <summary>Conditions that use every construct of the language; also the conditions the run-cost gate (<see cref="RunCostTests"/>) counts the loops of.</summary>
    internal static readonly string[] Written =
    [
        "close > 100", "close[2] > close", "rsi(14) > 55 and close > ema(20)", "not (rsi(14) > 55) or close < open", "crosses_above(close, vwap)", "crosses_below(tf(\"1h\").close[3], tf(\"1h\").ema(20))",
        "rising(close, 4)", "falling(close[3] + open, 6)", "between(close, 100, 200)", "abs(close - open) > min(high - low, 10) * max(1, 2)", "close / (high - low) > 1", "-close < -100 or (close > 1) == (open > 1)",
        "time >= \"09:45\"", "time < \"10:00\" and close > open", "regime() == \"UP_LOW_VOL\"", "day_of_week == TUE and is_expiry_day == 0", "crosses_above(vix, 20)", "rising(vix - index_trend, 3)",
        "rising(close[200], 50)", "crosses_above(close[254], vwap)",
    ];

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void The_written_conditions_call_only_the_kernel_reach_the_frame_through_its_three_doors_and_read_no_negative_lag(Timeframe evaluation)
    {
        var audited = 0;
        foreach (var source in Written)
        {
            var result = Compiling.Of(source, evaluation);
            if (result.Condition is null)
            {
                // only what the timeframe itself refuses (the time on a day, vwap on a day …) is not one of its conditions
                result.Errors.ShouldAllBe(e => e.Code == LintCodes.TimeOnDaily || e.Code == LintCodes.UnsupportedTimeframe, source + " on " + evaluation + " -> " + Compiling.Describe(result));
                continue;
            }

            Audit(source, evaluation).Violations.ShouldBeEmpty(source + " on " + evaluation);
            audited++;
        }

        audited.ShouldBeGreaterThanOrEqualTo(Written.Length - 6, evaluation + " refuses too many of the written conditions to say anything about them");
    }

    [Fact]
    public void The_written_conditions_between_them_call_every_door_and_the_audit_sees_the_calls()
    {
        var methods = Written.SelectMany(s => Audit(s, Timeframe.M15).Methods).ToHashSet();

        methods.ShouldBeSubsetOf(typeof(Kernel).GetMethods().Select(m => m.Name).Concat(["Abs", "Min", "Max"]));
        methods.ShouldContain(nameof(Kernel.Value));
        methods.ShouldContain(nameof(Kernel.ValueAt));
        methods.ShouldContain(nameof(Kernel.CloseMinute));
        methods.ShouldContain(nameof(Kernel.CrossesAbove));
        methods.ShouldContain(nameof(Kernel.StepRising));
        methods.ShouldContain(nameof(Kernel.Between));
        methods.ShouldContain("Abs");
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void The_generated_conditions_call_only_the_kernel_reach_the_frame_through_its_three_doors_and_read_no_negative_lag(Timeframe evaluation)
    {
        var violations = new List<string>();
        var methods = new HashSet<string>();
        foreach (var (withContext, seed) in new[] { (true, 21), (false, 22) })
        {
            var generator = new ModelGenerator(seed * 1_000 + (int)evaluation, evaluation, withContext, withContext);
            for (var i = 0; i < 150; i++)
            {
                var source = Printer.Print(generator.Next(), Parens.Minimal, Spacing.Normal);
                var report = Audit(source, evaluation, ModelGenerator.Parameters);
                methods.UnionWith(report.Methods);
                violations.AddRange(report.Violations.Select(v => source + " -> " + v));
            }
        }

        violations.ShouldBeEmpty(string.Join("\n", violations.Take(5)));
        methods.ShouldContain(nameof(Kernel.ValueAt));
        methods.ShouldContain(nameof(Kernel.CrossesBelow));
        methods.ShouldContain(nameof(Kernel.RunResult));
    }

    // ---- negative controls: each rule can fail ------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly ParameterExpression Frame = Expression.Parameter(typeof(FeatureFrame), "frame");

    private static readonly ParameterExpression Handles = Expression.Parameter(typeof(int[]), "handles");

    private static Expression<Decision> Tree(Expression body) => Expression.Lambda<Decision>(body, Frame, Handles);

    private static readonly Expression Handle = Expression.ArrayIndex(Handles, Expression.Constant(0));

    private static MethodCallExpression CallKernel(string name, params Expression[] arguments) => Expression.Call(typeof(Kernel).GetMethod(name)!, arguments);

    [Fact]
    public void A_tree_made_the_way_the_compiler_makes_it_passes_the_audit()
    {
        var valueAt = CallKernel(nameof(Kernel.ValueAt), Frame, Handle, Expression.Constant(2));
        var tree = Tree(CallKernel(nameof(Kernel.Greater), valueAt, CallKernel(nameof(Kernel.Value), Frame, Handle)));

        ExpressionAudit.Of(tree).Violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_call_of_anything_but_the_kernel_fails_the_audit()
    {
        var feed = Expression.Call(Frame, typeof(FeatureFrame).GetMethod(nameof(FeatureFrame.IsReady))!, Expression.Constant(0));
        var tree = Tree(Expression.Condition(feed, Expression.Constant(1), Expression.Constant(0)));

        ExpressionAudit.Of(tree).Violations.ShouldContain(v => v.Contains("FeatureFrame.IsReady", StringComparison.Ordinal));
    }

    [Fact]
    public void A_negative_lag_a_subtraction_in_the_lag_and_a_lag_from_a_negation_fail_the_audit()
    {
        var negative = Tree(CallKernel(nameof(Kernel.Greater), CallKernel(nameof(Kernel.ValueAt), Frame, Handle, Expression.Constant(-1)), Expression.Constant(1.0)));
        var subtracted = Tree(CallKernel(nameof(Kernel.Greater), CallKernel(nameof(Kernel.ValueAt), Frame, Handle, Expression.Subtract(Expression.Constant(3), Expression.Constant(5))), Expression.Constant(1.0)));
        var negated = Tree(CallKernel(nameof(Kernel.Greater), CallKernel(nameof(Kernel.ValueAt), Frame, Handle, Expression.Negate(Expression.Constant(1))), Expression.Constant(1.0)));

        foreach (var tree in new[] { negative, subtracted, negated })
        {
            ExpressionAudit.Of(tree).Violations.ShouldContain(v => v.Contains("below zero", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_frame_other_than_the_one_given_a_member_access_and_a_closure_fail_the_audit()
    {
        var other = Expression.Parameter(typeof(FeatureFrame), "other");
        var foreign = Tree(CallKernel(nameof(Kernel.Greater), CallKernel(nameof(Kernel.Value), other, Handle), Expression.Constant(1.0)));
        var member = Tree(Expression.Condition(Expression.GreaterThan(Expression.Property(Frame, nameof(FeatureFrame.Steps)), Expression.Constant(0L)), Expression.Constant(1), Expression.Constant(0)));
        var closure = Tree(Expression.Condition(Expression.Equal(Expression.Constant(new List<double>()), Expression.Constant(null, typeof(List<double>))), Expression.Constant(1), Expression.Constant(0)));

        ExpressionAudit.Of(foreign).Violations.ShouldContain(v => v.Contains("not given the frame", StringComparison.Ordinal));
        ExpressionAudit.Of(member).Violations.ShouldContain(v => v.Contains("MemberAccess", StringComparison.Ordinal));
        ExpressionAudit.Of(closure).Violations.ShouldContain(v => v.Contains("not a number", StringComparison.Ordinal));
    }
}
