using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>How much a printed model is bracketed.</summary>
internal enum Parens
{
    /// <summary>Every compound part is in brackets, so the text does not rely on the precedence rules.</summary>
    Always,

    /// <summary>Only where the grammar's precedence and associativity need them: the text relies on the parser's rules.</summary>
    Minimal,
}

/// <summary>How a printed model is spaced.</summary>
internal enum Spacing
{
    /// <summary>One space around operators and after commas.</summary>
    Normal,

    /// <summary>No space around the symbols and after commas (keywords keep theirs).</summary>
    Tight,

    /// <summary>Tabs around the symbols and a line break around <c>and</c> and <c>or</c> (whitespace is free in the language).</summary>
    Wide,
}

/// <summary>
/// Writes a model as source text, from the grammar of the spec alone. Precedence from loosest to tightest: <c>or</c> 1, <c>and</c> 2, <c>not</c> 3, comparison 4, <c>+ -</c> 5, <c>* /</c> 6, unary minus 7, primary 8. The
/// binary operators are left-associative, a comparison is not chained, and the operand of a comparison is a sum: the printer puts brackets where the tree could not otherwise be read back.
/// </summary>
internal static class Printer
{
    public static string Print(Node node, Parens parens = Parens.Always, Spacing spacing = Spacing.Normal) => new Writer(parens, spacing).Write(node, 0);

    private sealed class Writer(Parens parens, Spacing spacing)
    {
        private const int Primary = 8;

        public string Write(Node node, int minimum)
        {
            var (text, precedence) = Render(node);
            return precedence < minimum ? "(" + text + ")" : text;
        }

        /// <summary>The weakest precedence an operand may have without brackets.</summary>
        private int Needs(int minimum) => parens == Parens.Always ? Primary : minimum;

        private (string Text, int Precedence) Render(Node node)
        {
            switch (node)
            {
                case Lit lit:
                    return (lit.Text, Primary);
                case Const constant:
                    return (constant.Name, Primary);
                case BoolLit b:
                    return (b.Value ? "true" : "false", Primary);
                case Leaf leaf:
                    return (LeafText(leaf), Primary);
                case Neg neg:
                    {
                        var inner = Write(neg.Operand, Needs(7));
                        return ("-" + (inner.StartsWith('-') ? " " : string.Empty) + inner, 7);
                    }

                case Abs abs:
                    return ("abs(" + Write(abs.Operand, 0) + ")", Primary);
                case MinMax m:
                    return ((m.IsMax ? "max(" : "min(") + Write(m.Left, 0) + Comma() + Write(m.Right, 0) + ")", Primary);
                case Arith a:
                    {
                        var precedence = a.Op is '+' or '-' ? 5 : 6;
                        var left = Write(a.Left, Needs(precedence));
                        var right = Write(a.Right, Needs(precedence + 1));
                        return (left + Symbol(a.Op.ToString(), right) + right, precedence);
                    }

                case Not n:
                    return ("not " + Write(n.Operand, Needs(3)), 3);
                case Logic l:
                    {
                        var precedence = l.IsAnd ? 2 : 1;
                        var left = Write(l.Left, Needs(precedence));
                        var right = Write(l.Right, Needs(precedence + 1));
                        return (left + Keyword(l.IsAnd ? "and" : "or") + right, precedence);
                    }

                case Cmp c:
                    {
                        var left = Write(c.Left, Needs(5));
                        var right = Write(c.Right, Needs(5));
                        return (left + Symbol(c.Op, right) + right, 4);
                    }

                case Crosses c:
                    return ((c.Above ? "crosses_above(" : "crosses_below(") + Write(c.Left, 0) + Comma() + Write(c.Right, 0) + ")", Primary);
                case Streak s:
                    return ((s.Rising ? "rising(" : "falling(") + Write(s.Operand, 0) + Comma() + s.Count.ToString(CultureInfo.InvariantCulture) + ")", Primary);
                case Between b:
                    return ("between(" + Write(b.Value, 0) + Comma() + Write(b.Low, 0) + Comma() + Write(b.High, 0) + ")", Primary);
                case TimeCmp t:
                    {
                        var text = "\"" + t.Text + "\"";
                        return (t.Mirrored ? text + Symbol(Mirror(t.Op), "time") + "time" : "time" + Symbol(t.Op, text) + text, 4);
                    }

                case RegimeCmp r:
                    {
                        var operand = r.Name is null ? r.Code.ToString(CultureInfo.InvariantCulture) : "\"" + r.Name + "\"";
                        var op = r.Equal ? "==" : "!=";
                        return (r.NameOnLeft ? operand + Symbol(op, "regime()") + "regime()" : "regime()" + Symbol(op, operand) + operand, 4);
                    }

                default:
                    throw new InvalidOperationException("Unknown model node " + node);
            }
        }

        private static string LeafText(Leaf leaf)
        {
            var text = leaf.Qualified ? "tf(\"" + leaf.Frame.ToCode() + "\")." + leaf.Feature.QualifiedText : leaf.Feature.Text;
            return leaf.Lag > 0 ? text + "[" + leaf.Lag.ToString(CultureInfo.InvariantCulture) + "]" : text;
        }

        private string Comma() => spacing switch { Spacing.Tight => ",", Spacing.Wide => " ,\t", _ => ", " };

        /// <summary>An operator symbol with the spacing; <paramref name="next"/> is the text after it (a minus sign must not touch another).</summary>
        private string Symbol(string op, string next)
        {
            switch (spacing)
            {
                case Spacing.Tight:
                    return op == "-" && next.StartsWith('-') ? " - " : op;
                case Spacing.Wide:
                    return "\t" + op + "\t";
                default:
                    return " " + op + " ";
            }
        }

        private string Keyword(string word) => spacing == Spacing.Wide ? "\n" + word + "\n" : " " + word + " ";

        private static string Mirror(string op) => op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op };
    }
}
