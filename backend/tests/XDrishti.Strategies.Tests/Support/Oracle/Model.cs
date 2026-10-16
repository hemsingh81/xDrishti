using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>
/// A node of the expression <i>model</i> of the independent oracle: the meaning of a condition written down as a tree in the test project, before any text exists. The generator builds a model, <see cref="Printer"/> writes
/// it as source text for the compiler, and <see cref="Interpreter"/> evaluates the model directly against a feature frame. The model shares no type with the library under test (not the syntax tree, not the bound tree), so a
/// mistake of the parser, the binder or the compiler shows up as a difference between the two.
/// </summary>
internal abstract record Node;

/// <summary>A number written as a literal; <see cref="Text"/> is the spelling (<c>0.5</c>, <c>1e-3</c>).</summary>
internal sealed record Lit(string Text) : Node
{
    public double Value => double.Parse(Text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>A name that stands for a number known when the condition is compiled: a strategy parameter (<c>level</c>) or a weekday constant (<c>TUE</c>).</summary>
internal sealed record Const(string Name, double Value) : Node;

/// <summary><c>true</c> or <c>false</c>.</summary>
internal sealed record BoolLit(bool Value) : Node;

/// <summary>
/// A catalog feature as the DSL writes it: <see cref="Text"/> unqualified, <see cref="QualifiedText"/> after <c>tf("…").</c> (the flat catalog name for a dotted spelling), whether it is a reference that can take a subscript
/// (<c>close</c>) or a call (<c>rsi(14)</c>) that cannot, and the <see cref="Spec"/> on 1m (the timeframe is replaced by the leaf's).
/// </summary>
internal sealed record Feature(string Text, string QualifiedText, bool IsReference, FeatureSpec Spec, bool ReadsContext = false, bool ReadsExpiryDays = false);

/// <summary>A feature read on <see cref="Frame"/>, <see cref="Lag"/> closed bars back (the subscript), written with <c>tf(…)</c> when <see cref="Qualified"/>.</summary>
internal sealed record Leaf(Feature Feature, Timeframe Frame, bool Qualified, int Lag) : Node
{
    /// <summary>The spec the frame is asked for (not necessarily canonical: a source left out stays <see cref="PriceSource.Default"/>).</summary>
    public FeatureSpec Spec => Feature.Spec with { Timeframe = Frame };
}

/// <summary>Minus in front of a number.</summary>
internal sealed record Neg(Node Operand) : Node;

/// <summary><c>abs(x)</c>.</summary>
internal sealed record Abs(Node Operand) : Node;

/// <summary><c>min(a, b)</c> or <c>max(a, b)</c>.</summary>
internal sealed record MinMax(bool IsMax, Node Left, Node Right) : Node;

/// <summary>An arithmetic operator on two numbers: <c>+ - * /</c>.</summary>
internal sealed record Arith(char Op, Node Left, Node Right) : Node;

/// <summary><c>not x</c>.</summary>
internal sealed record Not(Node Operand) : Node;

/// <summary><c>a and b</c> or <c>a or b</c>.</summary>
internal sealed record Logic(bool IsAnd, Node Left, Node Right) : Node;

/// <summary>A comparison of two numbers or of two true/false values (<c>==</c> and <c>!=</c> only for the second kind).</summary>
internal sealed record Cmp(string Op, Node Left, Node Right) : Node;

/// <summary><c>crosses_above(a, b)</c> or <c>crosses_below(a, b)</c>.</summary>
internal sealed record Crosses(bool Above, Node Left, Node Right) : Node;

/// <summary><c>rising(x, n)</c> or <c>falling(x, n)</c>.</summary>
internal sealed record Streak(bool Rising, Node Operand, int Count) : Node;

/// <summary><c>between(x, lo, hi)</c>.</summary>
internal sealed record Between(Node Value, Node Low, Node High) : Node;

/// <summary>
/// <c>time</c> against a <c>"HH:MM"</c> text. <see cref="Op"/> is the operator with <c>time</c> on the left; when <see cref="Mirrored"/> the text is written first with the operator turned around
/// (<c>"09:45" &lt;= time</c> for <c>time &gt;= "09:45"</c>).
/// </summary>
internal sealed record TimeCmp(string Op, int MinuteOfDay, string Text, bool Mirrored) : Node;

/// <summary>
/// <c>regime()</c> equal or not equal to a regime: by <see cref="Name"/> (<c>"UP_LOW_VOL"</c>, on either side) or, when <see cref="Name"/> is null, by the code written as a number on the right. <see cref="Code"/> is the code the
/// oracle's own table gives for the name.
/// </summary>
internal sealed record RegimeCmp(bool Equal, int Code, string? Name, bool NameOnLeft, Leaf Regime) : Node;

/// <summary>What follows from how a condition is built: the features it reads, the history it needs and what the frame must be given. Worked out from the model, not from the library.</summary>
internal sealed record Facts(IReadOnlyList<FeatureSpec> Features, int HistoryDepth, bool NeedsContext, bool NeedsExpiryDays, bool UsesTime);

/// <summary>The facts of a model and the walk behind them.</summary>
internal static class ModelFacts
{
    public static Facts Of(Node root)
    {
        var reads = new List<(Leaf Leaf, int Lag)>();
        var time = false;
        Walk(root, 0, reads, ref time);
        var features = new List<FeatureSpec>();
        foreach (var (leaf, _) in reads)
        {
            if (!features.Contains(leaf.Spec))
            {
                features.Add(leaf.Spec);
            }
        }

        var deepest = reads.Count == 0 ? 0 : reads.Max(r => r.Lag);
        return new Facts(features, deepest + 1, reads.Any(r => r.Leaf.Feature.ReadsContext), reads.Any(r => r.Leaf.Feature.ReadsExpiryDays), time);
    }

    /// <summary>
    /// Every leaf with the lag it is read at: its own subscript plus the offset of the lagged calls around it (<c>crosses_*</c> reads its operands at 0 and 1, <c>rising(x, n)</c> at 0 to n).
    /// </summary>
    private static void Walk(Node node, int offset, List<(Leaf, int)> reads, ref bool time)
    {
        switch (node)
        {
            case Leaf leaf:
                reads.Add((leaf, leaf.Lag + offset));
                break;
            case Neg n:
                Walk(n.Operand, offset, reads, ref time);
                break;
            case Abs a:
                Walk(a.Operand, offset, reads, ref time);
                break;
            case MinMax m:
                Walk(m.Left, offset, reads, ref time);
                Walk(m.Right, offset, reads, ref time);
                break;
            case Arith a:
                Walk(a.Left, offset, reads, ref time);
                Walk(a.Right, offset, reads, ref time);
                break;
            case Not n:
                Walk(n.Operand, offset, reads, ref time);
                break;
            case Logic l:
                Walk(l.Left, offset, reads, ref time);
                Walk(l.Right, offset, reads, ref time);
                break;
            case Cmp c:
                Walk(c.Left, offset, reads, ref time);
                Walk(c.Right, offset, reads, ref time);
                break;
            case Crosses c:
                foreach (var lag in new[] { 0, 1 })
                {
                    Walk(c.Left, offset + lag, reads, ref time);
                    Walk(c.Right, offset + lag, reads, ref time);
                }

                break;
            case Streak s:
                foreach (var lag in new[] { 0, s.Count })
                {
                    Walk(s.Operand, offset + lag, reads, ref time);
                }

                break;
            case Between b:
                Walk(b.Value, offset, reads, ref time);
                Walk(b.Low, offset, reads, ref time);
                Walk(b.High, offset, reads, ref time);
                break;
            case TimeCmp:
                time = true;
                break;
            case RegimeCmp r:
                reads.Add((r.Regime, r.Regime.Lag + offset));
                break;
            case Lit or Const or BoolLit:
                break;
            default:
                throw new InvalidOperationException("Unknown model node " + node);
        }
    }
}
