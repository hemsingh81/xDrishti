using Superpower.Model;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// The nesting limit, checked in two places for two reasons (both give the code <see cref="SyntaxCodes.TooDeep"/> and the limit <see cref="ConditionParser.MaxNesting"/>). <b>Before the parse</b>, on the tokens: the
/// brackets (parentheses, calls and subscripts) that are open at once plus the prefix operators in a run (<c>not not x</c>, <c>- -x</c>) are what the parser recurses on, so more than the limit is refused before any
/// recursion starts. <b>After the parse</b>, on the tree: the depth of the tree (a chain <c>a and b and c …</c> is as deep as it is long) is what the printer, the binder and the compiler recurse on, so a tree deeper than the
/// limit is refused and nothing that walks the tree later can run out of stack. Both are measured without recursion.
/// </summary>
internal static class Nesting
{
    /// <summary>The first token at which the open brackets and the pending prefix operators exceed the limit, or null.</summary>
    public static Token<Tk>? FirstTooDeep(IReadOnlyList<Token<Tk>> tokens)
    {
        // depth = what each open bracket stands for (itself plus the prefix operators written just before it, which are still waiting for their operand) + the prefix operators pending now.
        var depth = 0;
        var pending = 0;
        var open = new Stack<int>();
        var previous = Tk.None;
        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case Tk.LeftParen or Tk.LeftBracket:
                    open.Push(1 + pending);
                    pending = 0;
                    depth++;
                    break;
                case Tk.RightParen or Tk.RightBracket:
                    depth -= pending;
                    pending = 0;
                    if (open.Count > 0)
                    {
                        depth -= open.Pop();
                    }

                    break;
                case Tk.Not:
                case Tk.Minus when !TokenRules.EndsOperand(previous):
                    pending++;
                    depth++;
                    break;
                default:
                    depth -= pending;
                    pending = 0;
                    break;
            }

            if (depth > ConditionParser.MaxNesting)
            {
                return token;
            }

            previous = token.Kind;
        }

        return null;
    }

    /// <summary>The depth of the deepest node of the tree (the root is 0), found without recursion; stops counting once <paramref name="limit"/> is passed.</summary>
    public static int Depth(Expression root, int limit)
    {
        var deepest = 0;
        var pending = new Stack<(Expression Node, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0 && deepest <= limit)
        {
            var (node, depth) = pending.Pop();
            deepest = Math.Max(deepest, depth);
            switch (node)
            {
                case Binary binary:
                    pending.Push((binary.Left, depth + 1));
                    pending.Push((binary.Right, depth + 1));
                    break;
                case Unary unary:
                    pending.Push((unary.Operand, depth + 1));
                    break;
                case Subscript subscript:
                    pending.Push((subscript.Target, depth + 1));
                    break;
                case Call call:
                    foreach (var argument in call.Arguments)
                    {
                        if (argument is PositionalArgument positional)
                        {
                            pending.Push((positional.Value, depth + 1));
                        }
                    }

                    break;
            }
        }

        return deepest;
    }
}
