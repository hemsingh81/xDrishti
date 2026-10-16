using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>The three values of a condition, in the oracle's own type (it does not use the library's).</summary>
internal enum Tri
{
    False,
    True,
    Unknown,
}

/// <summary>
/// The independent oracle (spec P3-02, criterion 7): a tree-walking interpreter of the <see cref="Node"/> model, written from the <i>Definitions</i> of the spec and from nothing else. It reads a
/// <see cref="FeatureFrame"/> only through its public members (<c>HandleOf</c>, <c>this[]</c>, <c>At</c>, <c>IsReadyAt</c>, <c>Steps</c>, <c>LastBarTime</c>) and calls no code of the Syntax, Binding, Lint or Compilation
/// namespaces. What it implements, each point taken from the spec:
/// <list type="bullet">
/// <item>a number is a double and <i>not ready</i> is NaN; arithmetic with a not-ready operand is not ready; <c>x / 0</c> is not ready; <c>min</c> and <c>max</c> and <c>abs</c> pass a NaN on;</item>
/// <item>a comparison, <c>crosses_*</c>, <c>rising</c>, <c>falling</c>, <c>between</c> and <c>time</c> are <i>unknown</i> when any operand they need is not ready — all of them, not only the deciding ones;</item>
/// <item><c>and</c>, <c>or</c>, <c>not</c> follow Kleene: <c>false and unknown</c> is false, <c>true or unknown</c> is true, <c>not unknown</c> is unknown;</item>
/// <item>an operand of a lagged call is evaluated <i>at lag k</i>: every feature leaf is read with <c>At(handle, own subscript + k)</c>, constants stay constants;
/// <c>crosses_above(a, b)</c> is <c>a[0] &gt; b[0] and a[1] &lt;= b[1]</c>, <c>rising(x, n)</c> is <c>x[0] &gt; x[1] &gt; … &gt; x[n]</c>; <c>between</c> includes both ends;</item>
/// <item><c>time</c> is the India clock time at which the evaluated bar closed: the start of the last accepted 1-minute bar plus one minute, in whole minutes; unknown while the frame has taken no bar;</item>
/// <item><c>regime()</c> is the feature <c>regime</c> (1 to 9) and a name is resolved by the oracle's own table of the nine names.</item>
/// </list>
/// </summary>
internal sealed class Interpreter(FeatureFrame frame)
{
    /// <summary>India time is UTC+05:30: 19,800 seconds.</summary>
    private const long IndiaOffsetSeconds = 5 * 3600 + 30 * 60;

    private readonly Dictionary<FeatureSpec, int> _handles = [];

    /// <summary>The value of a true/false node now.</summary>
    public Tri Evaluate(Node node) => Truth(node);

    private int Handle(FeatureSpec spec)
    {
        if (!_handles.TryGetValue(spec, out var handle))
        {
            handle = frame.HandleOf(spec);
            if (handle < 0)
            {
                throw new InvalidOperationException("The frame has no feature " + spec + ".");
            }

            _handles[spec] = handle;
        }

        return handle;
    }

    /// <summary>The value of a leaf <paramref name="extra"/> bars further back than it is written; NaN while it is not ready.</summary>
    private double Read(Leaf leaf, int extra)
    {
        var handle = Handle(leaf.Spec);
        var lag = leaf.Lag + extra;
        if (!frame.IsReadyAt(handle, lag))
        {
            return double.NaN;
        }

        return lag == 0 ? frame[handle] : frame.At(handle, lag);
    }

    // ---- numbers ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private double Number(Node node, int extra)
    {
        switch (node)
        {
            case Lit lit:
                return lit.Value;
            case Const constant:
                return constant.Value;
            case Leaf leaf:
                return Read(leaf, extra);
            case Neg neg:
                return -Number(neg.Operand, extra);
            case Abs abs:
                {
                    var x = Number(abs.Operand, extra);
                    return x < 0 ? -x : x;
                }

            case MinMax m:
                {
                    var a = Number(m.Left, extra);
                    var b = Number(m.Right, extra);
                    if (double.IsNaN(a) || double.IsNaN(b))
                    {
                        return double.NaN;
                    }

                    return m.IsMax ? (a > b ? a : b) : (a < b ? a : b);
                }

            case Arith arith:
                {
                    var a = Number(arith.Left, extra);
                    var b = Number(arith.Right, extra);
                    if (double.IsNaN(a) || double.IsNaN(b))
                    {
                        return double.NaN;
                    }

                    return arith.Op switch
                    {
                        '+' => a + b,
                        '-' => a - b,
                        '*' => a * b,
                        '/' => b == 0 ? double.NaN : a / b,
                        _ => throw new InvalidOperationException("Unknown operator " + arith.Op),
                    };
                }

            default:
                throw new InvalidOperationException("Not a number: " + node);
        }
    }

    // ---- true / false / unknown ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static bool IsTruthValued(Node node) =>
        node is BoolLit or Not or Logic or Cmp or Crosses or Streak or Between or TimeCmp or RegimeCmp;

    private static Tri Of(bool holds) => holds ? Tri.True : Tri.False;

    private static Tri Negate(Tri value) => value switch { Tri.True => Tri.False, Tri.False => Tri.True, _ => Tri.Unknown };

    private static Tri And(Tri a, Tri b) =>
        a == Tri.False || b == Tri.False ? Tri.False : a == Tri.Unknown || b == Tri.Unknown ? Tri.Unknown : Tri.True;

    private static Tri Or(Tri a, Tri b) =>
        a == Tri.True || b == Tri.True ? Tri.True : a == Tri.Unknown || b == Tri.Unknown ? Tri.Unknown : Tri.False;

    private Tri Truth(Node node)
    {
        switch (node)
        {
            case BoolLit b:
                return Of(b.Value);
            case Not n:
                return Negate(Truth(n.Operand));
            case Logic l:
                {
                    var left = Truth(l.Left);
                    var right = Truth(l.Right);
                    return l.IsAnd ? And(left, right) : Or(left, right);
                }

            case Cmp c:
                return Compare(c);
            case Crosses c:
                {
                    var a0 = Number(c.Left, 0);
                    var b0 = Number(c.Right, 0);
                    var a1 = Number(c.Left, 1);
                    var b1 = Number(c.Right, 1);
                    if (AnyNaN(a0, b0, a1, b1))
                    {
                        return Tri.Unknown;
                    }

                    return Of(c.Above ? a0 > b0 && a1 <= b1 : a0 < b0 && a1 >= b1);
                }

            case Streak s:
                {
                    var values = new double[s.Count + 1];
                    for (var k = 0; k <= s.Count; k++)
                    {
                        values[k] = Number(s.Operand, k);
                    }

                    if (AnyNaN(values))
                    {
                        return Tri.Unknown;
                    }

                    for (var k = 0; k < s.Count; k++)
                    {
                        if (s.Rising ? !(values[k] > values[k + 1]) : !(values[k] < values[k + 1]))
                        {
                            return Tri.False;
                        }
                    }

                    return Tri.True;
                }

            case Between b:
                {
                    var x = Number(b.Value, 0);
                    var low = Number(b.Low, 0);
                    var high = Number(b.High, 0);
                    return AnyNaN(x, low, high) ? Tri.Unknown : Of(low <= x && x <= high);
                }

            case TimeCmp t:
                return CompareTime(t);
            case RegimeCmp r:
                {
                    var value = Read(r.Regime, 0);
                    if (double.IsNaN(value))
                    {
                        return Tri.Unknown;
                    }

                    var same = value == r.Code;
                    return Of(r.Equal ? same : !same);
                }

            default:
                throw new InvalidOperationException("Not a true/false value: " + node);
        }
    }

    private static bool AnyNaN(params double[] values)
    {
        foreach (var value in values)
        {
            if (double.IsNaN(value))
            {
                return true;
            }
        }

        return false;
    }

    private Tri Compare(Cmp c)
    {
        if (IsTruthValued(c.Left) && IsTruthValued(c.Right))
        {
            var left = Truth(c.Left);
            var right = Truth(c.Right);
            if (left == Tri.Unknown || right == Tri.Unknown)
            {
                return Tri.Unknown;
            }

            return Of(c.Op switch
            {
                "==" => left == right,
                "!=" => left != right,
                _ => throw new InvalidOperationException("Not an operator of true/false values: " + c.Op),
            });
        }

        var a = Number(c.Left, 0);
        var b = Number(c.Right, 0);
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return Tri.Unknown;
        }

        return Of(Holds(c.Op, a, b));
    }

    private static bool Holds(string op, double a, double b) => op switch
    {
        "<" => a < b,
        "<=" => a <= b,
        ">" => a > b,
        ">=" => a >= b,
        "==" => a == b,
        "!=" => a != b,
        _ => throw new InvalidOperationException("Unknown comparison " + op),
    };

    /// <summary>The India minute of the day at which the last accepted bar closed, against the text; unknown before the first bar.</summary>
    private Tri CompareTime(TimeCmp t)
    {
        if (frame.Steps == 0)
        {
            return Tri.Unknown;
        }

        var closedAt = frame.LastBarTime + 60 + IndiaOffsetSeconds;
        var secondOfDay = ((closedAt % 86_400) + 86_400) % 86_400;
        var minute = (int)(secondOfDay / 60);
        return Of(Holds(t.Op, minute, t.MinuteOfDay));
    }
}
