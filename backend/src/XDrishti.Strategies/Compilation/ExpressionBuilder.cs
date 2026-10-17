using System.Linq.Expressions;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using BinaryOperator = XDrishti.Strategies.Syntax.BinaryOperator;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// Turns a bound tree into a <see cref="Decision"/>: a <c>System.Linq.Expressions</c> tree built from the validated nodes, never from text, and compiled once to a delegate. This is the only dynamic code of the library
/// (spec P3-02, <i>Security</i>). The tree has two kinds of node. A <b>number</b> is an expression of type <see cref="double"/> (NaN when not ready): a constant, a feature read through the frame, arithmetic, <c>abs</c>,
/// <c>min</c>, <c>max</c>; a number is built <i>at an offset</i> — how many closed bars further back than written every feature in it is read —, which is how <c>crosses_*</c> and <c>rising</c>/<c>falling</c> evaluate
/// their operands at several lags while constants stay constants. A <b>truth</b> is an expression of type <see cref="int"/> (the numbers of <see cref="Truth"/>): comparisons and the lagged builtins call the functions of
/// <see cref="Kernel"/> (so "unknown" is decided in one place), <c>and</c>/<c>or</c> skip their right side when the left one decides, and <c>not</c> follows Kleene's table.
/// <para>
/// The compiled delegate takes the frame and the handles as arguments, so it holds no frame and no handle: one delegate serves every frame the condition is bound to. It allocates nothing (every constant is
/// an immediate, there is no closure over data, no boxing and no LINQ at run time). The recursion follows the depth of the tree, which the parser limits to 64 levels.
/// </para>
/// </summary>
internal sealed class ExpressionBuilder
{
    private static readonly ConstantExpression NoOffset = Expression.Constant(0);

    private readonly ParameterExpression _frame = Expression.Parameter(typeof(FeatureFrame), "frame");

    private readonly ParameterExpression _handles = Expression.Parameter(typeof(int[]), "handles");

    private readonly Dictionary<FeatureSpec, int> _slotOf;

    private ExpressionBuilder(IReadOnlyList<FeatureSpec> features)
    {
        _slotOf = new Dictionary<FeatureSpec, int>(features.Count);
        for (var i = 0; i < features.Count; i++)
        {
            _slotOf.Add(features[i], i);
        }
    }

    /// <summary>Compiles the tree; <paramref name="features"/> are the distinct specs of the condition, and the delegate reads <c>handles[i]</c> as the handle of <c>features[i]</c>.</summary>
    public static Decision Build(IBoundExpression root, IReadOnlyList<FeatureSpec> features) => Lambda(root, features).Compile();

    /// <summary>
    /// The expression tree of the condition, before it is compiled. Nothing but <see cref="Build"/> needs it at run time; the tests walk it to prove which functions a condition can call and how it reads the frame
    /// (only through <see cref="Kernel"/>, with a lag that is never below the one written).
    /// </summary>
    public static Expression<Decision> Lambda(IBoundExpression root, IReadOnlyList<FeatureSpec> features)
    {
        var builder = new ExpressionBuilder(features);
        var body = builder.TruthOf(root);
        return Expression.Lambda<Decision>(body, builder._frame, builder._handles);
    }

    private static InvalidOperationException Unexpected(IBoundExpression node) =>
        new("A " + node.GetType().Name + " cannot be compiled: the compiler only receives a bound tree without errors.");

    private static ConstantExpression TruthConstant(int value) => Expression.Constant(value);

    // ---- truth ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private Expression TruthOf(IBoundExpression node) => node switch
    {
        BoundBool boolean => TruthConstant(boolean.Value ? Kernel.True : Kernel.False),
        BoundNot not => Expression.Call(KernelMethods.Not, TruthOf(not.Operand)),
        BoundLogical logical => Logical(logical),
        BoundCompare compare => Compare(compare),
        BoundTimeCompare time => Expression.Call(KernelMethods.Comparison(time.Operator), Expression.Call(KernelMethods.CloseMinute, _frame), Expression.Constant((double)time.MinuteOfDay)),
        BoundCrossing crossing => Crossing(crossing),
        BoundRun run => Run(run),
        BoundBetween between => Expression.Call(KernelMethods.Between, Number(between.Value, NoOffset), Number(between.Low, NoOffset), Number(between.High, NoOffset)),
        _ => throw Unexpected(node),
    };

    /// <summary><c>a and b</c> / <c>a or b</c>: the right side is evaluated only when the left one does not decide (false for <c>and</c>, true for <c>or</c>).</summary>
    private BlockExpression Logical(BoundLogical logical)
    {
        var isAnd = logical.Operator == BinaryOperator.And;
        var decided = isAnd ? Kernel.False : Kernel.True;
        var left = Expression.Variable(typeof(int), "left");
        return Expression.Block(
            typeof(int),
            [left],
            Expression.Assign(left, TruthOf(logical.Left)),
            Expression.Condition(
                Expression.Equal(left, TruthConstant(decided)),
                TruthConstant(decided),
                Expression.Call(isAnd ? KernelMethods.And : KernelMethods.Or, left, TruthOf(logical.Right))));
    }

    private MethodCallExpression Compare(BoundCompare compare)
    {
        if (compare.Left.Type == BoundType.Bool)
        {
            return Expression.Call(compare.Operator == BinaryOperator.Equal ? KernelMethods.SameTruth : KernelMethods.DifferentTruth, TruthOf(compare.Left), TruthOf(compare.Right));
        }

        return Expression.Call(KernelMethods.Comparison(compare.Operator), Number(compare.Left, NoOffset), Number(compare.Right, NoOffset));
    }

    /// <summary><c>crosses_above(a, b)</c>: the four values <c>a[0]</c>, <c>b[0]</c>, <c>a[1]</c>, <c>b[1]</c>, with every feature in each side read one closed bar further back for the second pair.</summary>
    private MethodCallExpression Crossing(BoundCrossing crossing)
    {
        var previous = Expression.Constant(1);
        return Expression.Call(
            crossing.IsAbove ? KernelMethods.CrossesAbove : KernelMethods.CrossesBelow,
            Number(crossing.Left, NoOffset),
            Number(crossing.Right, NoOffset),
            Number(crossing.Left, previous),
            Number(crossing.Right, previous));
    }

    /// <summary>
    /// <c>rising(x, n)</c> / <c>falling(x, n)</c>: a loop over the lags 1 to n (the operand is built once, with the loop counter as its offset) that compares each value with the one before it and remembers whether a step
    /// failed and whether a value was not ready.
    /// </summary>
    private BlockExpression Run(BoundRun run)
    {
        var newer = Expression.Variable(typeof(double), "newer");
        var older = Expression.Variable(typeof(double), "older");
        var state = Expression.Variable(typeof(int), "state");
        var lag = Expression.Variable(typeof(int), "lag");
        var done = Expression.Label("done");
        var step = run.IsRising ? KernelMethods.StepRising : KernelMethods.StepFalling;

        // No early exit, on purpose: a run is atomic over NaN (a missing value anywhere makes it unknown, even after a step has failed), so every step is read and the work is n x reads whatever the data.
        var loop = Expression.Loop(
            Expression.Block(
                Expression.IfThen(Expression.GreaterThan(lag, Expression.Constant(run.Count)), Expression.Break(done)),
                Expression.Assign(older, Number(run.Operand, lag)),
                Expression.Assign(state, Expression.Or(state, Expression.Call(step, newer, older))),
                Expression.Assign(newer, older),
                Expression.PreIncrementAssign(lag)),
            done);
        return Expression.Block(
            typeof(int),
            [newer, older, state, lag],
            Expression.Assign(newer, Number(run.Operand, NoOffset)),
            Expression.Assign(state, Expression.Call(KernelMethods.RunStart, newer)),
            Expression.Assign(lag, Expression.Constant(1)),
            loop,
            Expression.Call(KernelMethods.RunResult, state));
    }

    // ---- numbers --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The number as an expression of type double; every feature in it is read <paramref name="offset"/> closed bars (an int expression) further back than written.</summary>
    private Expression Number(IBoundExpression node, Expression offset) => node switch
    {
        BoundNumber number => Expression.Constant(number.Value),
        BoundFeature feature => Read(feature, offset),
        BoundNegate negate => Expression.Negate(Number(negate.Operand, offset)),
        BoundArithmetic arithmetic => Arithmetic(arithmetic, offset),
        BoundAbs abs => Expression.Call(KernelMethods.Abs, Number(abs.Operand, offset)),
        BoundMinMax minMax => Expression.Call(minMax.IsMax ? KernelMethods.Max : KernelMethods.Min, Number(minMax.Left, offset), Number(minMax.Right, offset)),
        _ => throw Unexpected(node),
    };

    private Expression Arithmetic(BoundArithmetic arithmetic, Expression offset)
    {
        var left = Number(arithmetic.Left, offset);
        var right = Number(arithmetic.Right, offset);
        return arithmetic.Operator switch
        {
            BinaryOperator.Add => Expression.Add(left, right),
            BinaryOperator.Subtract => Expression.Subtract(left, right),
            BinaryOperator.Multiply => Expression.Multiply(left, right),
            BinaryOperator.Divide => Expression.Call(KernelMethods.Divide, left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(arithmetic), arithmetic.Operator, "Not an arithmetic operator."),
        };
    }

    /// <summary>A feature at its own lag plus the offset: the frame's value for lag 0, <c>At(handle, lag)</c> otherwise (a lag known at compile time is a constant).</summary>
    private MethodCallExpression Read(BoundFeature feature, Expression offset)
    {
        var handle = Expression.ArrayIndex(_handles, Expression.Constant(_slotOf[feature.Spec]));
        if (offset is ConstantExpression { Value: int by })
        {
            var lag = feature.Lag + by;
            return lag == 0
                ? Expression.Call(KernelMethods.Value, _frame, handle)
                : Expression.Call(KernelMethods.ValueAt, _frame, handle, Expression.Constant(lag));
        }

        return Expression.Call(KernelMethods.ValueAt, _frame, handle, Expression.Add(Expression.Constant(feature.Lag), offset));
    }
}
