using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>
/// The same oracle as <see cref="Interpreter"/>, over the <i>bound</i> tree the compiler is built from (<c>CompiledCondition.Tree</c>) instead of over the model: a tree-walking interpreter written from the spec's
/// Definitions that reads the frame through its public members and calls nothing of <c>XDrishti.Strategies.Compilation</c>. It exists to tell where a difference comes from: when the model interpreter and this one
/// agree and the compiled delegate differs, the compiler is wrong; when the two interpreters differ, the binder (or the printer's text) is. The per-session features are read from the
/// <see cref="SessionReference"/>, as in <see cref="Interpreter"/>.
/// </summary>
internal sealed class BoundTreeInterpreter(FeatureFrame frame, SessionReference? sessions = null)
{
    private const long IndiaOffsetSeconds = 19_800;

    private readonly Dictionary<FeatureSpec, int> _handles = [];

    public Tri Evaluate(IBoundExpression node) => Truth(node);

    private double Read(BoundFeature feature, int extra)
    {
        if (!_handles.TryGetValue(feature.Spec, out var handle))
        {
            handle = frame.HandleOf(feature.Spec);
            if (handle < 0)
            {
                throw new InvalidOperationException("The frame has no feature " + feature.Spec + ".");
            }

            _handles[feature.Spec] = handle;
        }

        var lag = feature.Lag + extra;
        if (SessionReference.Covers(feature.Spec))
        {
            return (sessions ?? throw new InvalidOperationException("The condition reads " + feature.Spec + ", a per-session feature: the interpreter needs a SessionReference.")).Read(frame.SpecOf(handle), lag);
        }

        if (!frame.IsReadyAt(handle, lag))
        {
            return double.NaN;
        }

        return lag == 0 ? frame[handle] : frame.At(handle, lag);
    }

    private double Number(IBoundExpression node, int extra)
    {
        switch (node)
        {
            case BoundNumber number:
                return number.Value;
            case BoundFeature feature:
                return Read(feature, extra);
            case BoundNegate negate:
                return -Number(negate.Operand, extra);
            case BoundAbs abs:
                {
                    var x = Number(abs.Operand, extra);
                    return x < 0 ? -x : x;
                }

            case BoundMinMax minMax:
                {
                    var a = Number(minMax.Left, extra);
                    var b = Number(minMax.Right, extra);
                    if (double.IsNaN(a) || double.IsNaN(b))
                    {
                        return double.NaN;
                    }

                    return minMax.IsMax ? (a > b ? a : b) : (a < b ? a : b);
                }

            case BoundArithmetic arithmetic:
                {
                    var a = Number(arithmetic.Left, extra);
                    var b = Number(arithmetic.Right, extra);
                    if (double.IsNaN(a) || double.IsNaN(b))
                    {
                        return double.NaN;
                    }

                    return arithmetic.Operator switch
                    {
                        BinaryOperator.Add => a + b,
                        BinaryOperator.Subtract => a - b,
                        BinaryOperator.Multiply => a * b,
                        BinaryOperator.Divide => b == 0 ? double.NaN : a / b,
                        _ => throw new InvalidOperationException("Not an arithmetic operator: " + arithmetic.Operator),
                    };
                }

            default:
                throw new InvalidOperationException("Not a number: " + node);
        }
    }

    private static Tri Of(bool holds) => holds ? Tri.True : Tri.False;

    private static bool AnyNaN(params double[] values) => values.Any(double.IsNaN);

    private Tri Truth(IBoundExpression node)
    {
        switch (node)
        {
            case BoundBool b:
                return Of(b.Value);
            case BoundNot not:
                return Truth(not.Operand) switch { Tri.True => Tri.False, Tri.False => Tri.True, _ => Tri.Unknown };
            case BoundLogical logical:
                {
                    var left = Truth(logical.Left);
                    var right = Truth(logical.Right);
                    if (logical.Operator == BinaryOperator.And)
                    {
                        return left == Tri.False || right == Tri.False ? Tri.False : left == Tri.Unknown || right == Tri.Unknown ? Tri.Unknown : Tri.True;
                    }

                    return left == Tri.True || right == Tri.True ? Tri.True : left == Tri.Unknown || right == Tri.Unknown ? Tri.Unknown : Tri.False;
                }

            case BoundCompare compare:
                return Compare(compare);
            case BoundTimeCompare time:
                {
                    if (frame.Steps == 0)
                    {
                        return Tri.Unknown;
                    }

                    var closedAt = frame.LastBarTime + 60 + IndiaOffsetSeconds;
                    var minute = (int)((((closedAt % 86_400) + 86_400) % 86_400) / 60);
                    return Of(Holds(time.Operator, minute, time.MinuteOfDay));
                }

            case BoundCrossing crossing:
                {
                    var a0 = Number(crossing.Left, 0);
                    var b0 = Number(crossing.Right, 0);
                    var a1 = Number(crossing.Left, 1);
                    var b1 = Number(crossing.Right, 1);
                    if (AnyNaN(a0, b0, a1, b1))
                    {
                        return Tri.Unknown;
                    }

                    return Of(crossing.IsAbove ? a0 > b0 && a1 <= b1 : a0 < b0 && a1 >= b1);
                }

            case BoundRun run:
                {
                    var values = new double[run.Count + 1];
                    for (var k = 0; k <= run.Count; k++)
                    {
                        values[k] = Number(run.Operand, k);
                    }

                    if (AnyNaN(values))
                    {
                        return Tri.Unknown;
                    }

                    for (var k = 0; k < run.Count; k++)
                    {
                        if (run.IsRising ? !(values[k] > values[k + 1]) : !(values[k] < values[k + 1]))
                        {
                            return Tri.False;
                        }
                    }

                    return Tri.True;
                }

            case BoundBetween between:
                {
                    var x = Number(between.Value, 0);
                    var low = Number(between.Low, 0);
                    var high = Number(between.High, 0);
                    return AnyNaN(x, low, high) ? Tri.Unknown : Of(low <= x && x <= high);
                }

            default:
                throw new InvalidOperationException("Not a true/false value: " + node);
        }
    }

    private Tri Compare(BoundCompare compare)
    {
        if (compare.Left.Type == BoundType.Bool)
        {
            var left = Truth(compare.Left);
            var right = Truth(compare.Right);
            if (left == Tri.Unknown || right == Tri.Unknown)
            {
                return Tri.Unknown;
            }

            return Of(compare.Operator == BinaryOperator.Equal ? left == right : left != right);
        }

        var a = Number(compare.Left, 0);
        var b = Number(compare.Right, 0);
        return double.IsNaN(a) || double.IsNaN(b) ? Tri.Unknown : Of(Holds(compare.Operator, a, b));
    }

    private static bool Holds(BinaryOperator op, double a, double b) => op switch
    {
        BinaryOperator.Less => a < b,
        BinaryOperator.LessOrEqual => a <= b,
        BinaryOperator.Greater => a > b,
        BinaryOperator.GreaterOrEqual => a >= b,
        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        _ => throw new InvalidOperationException("Not a comparison: " + op),
    };
}
