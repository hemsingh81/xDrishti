using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// The grammar of the rules language over the tokens of <see cref="Lexer"/> (the grammar and its precedence are in the spec P3-02 and in docs/design/11-strategy-lab.md §2), built from Superpower parsers. The
/// parsers hold no state, so one set serves every thread. A comparison is not chained, a subscript is accepted only right after a reference, and a <see cref="Tk.Missing"/> token stands in for whatever the recovery of
/// <see cref="Recovery"/> had to supply, so one problem does not hide the next.
/// <para>
/// <b>Two kinds of parser.</b> The small productions that do not nest (names, literals, <c>tf("1h").</c>, a named argument, a subscript) are combinators. The <b>recursive spine</b> (an expression with its
/// operators, a parenthesised expression, the arguments of a call) is written as plain Superpower parser functions with precedence climbing, not as one combinator layer per precedence level: stack use is what limits
/// the nesting. Measured by parsing 64 nested calls (the deepest accepted text) on threads of a fixed stack size, nine combinator layers needed more than 1 MB in a Debug build; this spine needs a thread stack of 512 KB in
/// a Release build and 768 KB in a Debug build, figures that include the 128 KB the runtime keeps in reserve for the check below (a default thread has 1.4 MB usable on macOS and 8 MB on Linux). Each level of nesting
/// checks that enough stack is left and fails the parse (a <c>too_deep</c> problem) instead of overflowing. Failures are reported where the first token that cannot continue the expression is, which is what
/// <see cref="Recovery"/> reads.
/// </para>
/// </summary>
internal static class Grammar
{
    private const int LevelOr = 1;
    private const int LevelAnd = 2;
    private const int LevelNot = 3;
    private const int LevelCompare = 4;
    private const int LevelSum = 5;
    private const int LevelProduct = 6;
    private const int LevelAtom = 8;

    private static readonly TokenListParser<Tk, Identifier> Name =
        Token.EqualTo(Tk.Identifier).Or(Token.EqualTo(Tk.Missing)).Select(t => new Identifier(t.Kind == Tk.Missing ? string.Empty : TextOf(t), SpanOf(t)));

    private static readonly TokenListParser<Tk, StringLiteral> QuotedText =
        Token.EqualTo(Tk.String).Or(Token.EqualTo(Tk.Missing)).Select(t => new StringLiteral(t.Kind == Tk.Missing ? string.Empty : Unquote(t), SpanOf(t)));

    private static readonly TokenListParser<Tk, Argument> NamedArgumentParser =
        from name in Name
        from assign in Token.EqualTo(Tk.Assign)
        from value in Name
        select (Argument)new NamedArgument(name, value, name.Span.To(value.Span));

    /// <summary><c>("1h").name</c> after <c>tf</c>: the timeframe string and the name of the feature.</summary>
    private static readonly TokenListParser<Tk, (StringLiteral Frame, Identifier Member)> QualifierTail =
        from open in Token.EqualTo(Tk.LeftParen)
        from frame in QuotedText
        from close in Token.EqualTo(Tk.RightParen)
        from dot in Token.EqualTo(Tk.Dot)
        from member in Name
        select (frame, member);

    /// <summary><c>[1]</c> or <c>[-1]</c>: the number and the closing bracket. Anything else between the brackets is a failure that the recovery explains.</summary>
    private static readonly TokenListParser<Tk, (int Lag, SourceSpan LagSpan, SourceSpan End)> SubscriptTail =
        from open in Token.EqualTo(Tk.LeftBracket)
        from lag in LagNumber()
        from close in Token.EqualTo(Tk.RightBracket)
        select (lag.Value, lag.Span, SpanOf(close));

    /// <summary>A whole condition: one expression and nothing after it.</summary>
    public static TokenListParserResult<Tk, Expression> Condition(TokenList<Tk> input)
    {
        var expression = Climb(input, 0);
        if (!expression.HasValue)
        {
            return expression;
        }

        return expression.Remainder.IsAtEnd ? expression : TokenListParserResult.Empty<Tk, Expression>(expression.Remainder);
    }

    public static SourceSpan SpanOf(Token<Tk> token) =>
        new(token.Position.Absolute, token.Position.Absolute + token.Span.Length, token.Position.Line, token.Position.Column);

    /// <summary>The source text of a token.</summary>
    public static string TextOf(Token<Tk> token) => token.ToStringValue() ?? string.Empty;

    /// <summary>
    /// An expression whose operators bind at least as tightly as <paramref name="minimum"/>: the operand, then operators while they are tight enough. An operator that is tighter than what is already built on its
    /// left (<c>a and b &lt; c</c> after a failed chain) or a second comparison does not extend it, so the caller stops there and the whole parse fails at that operator.
    /// </summary>
    private static TokenListParserResult<Tk, Expression> Climb(TokenList<Tk> input, int minimum)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        var first = Operand(input, minimum, out var leftLevel);
        if (!first.HasValue)
        {
            return first;
        }

        var left = first.Value;
        var rest = first.Remainder;
        while (true)
        {
            var next = rest.ConsumeToken();
            if (!next.HasValue || !TryInfix(next.Value.Kind, out var level, out var op) || level < minimum)
            {
                break;
            }

            if (level > leftLevel || (level == LevelCompare && leftLevel == LevelCompare))
            {
                break;
            }

            var right = Climb(next.Remainder, level + 1);
            if (!right.HasValue)
            {
                return right;
            }

            left = new Binary(op, left, right.Value, left.Span.To(right.Value.Span));
            leftLevel = level;
            rest = right.Remainder;
        }

        return TokenListParserResult.Value(left, input, rest);
    }

    private static bool TryInfix(Tk kind, out int level, out BinaryOperator op)
    {
        (level, op) = kind switch
        {
            Tk.Or => (LevelOr, BinaryOperator.Or),
            Tk.And => (LevelAnd, BinaryOperator.And),
            Tk.Less => (LevelCompare, BinaryOperator.Less),
            Tk.LessOrEqual => (LevelCompare, BinaryOperator.LessOrEqual),
            Tk.Greater => (LevelCompare, BinaryOperator.Greater),
            Tk.GreaterOrEqual => (LevelCompare, BinaryOperator.GreaterOrEqual),
            Tk.Equal => (LevelCompare, BinaryOperator.Equal),
            Tk.NotEqual => (LevelCompare, BinaryOperator.NotEqual),
            Tk.Plus => (LevelSum, BinaryOperator.Add),
            Tk.Minus => (LevelSum, BinaryOperator.Subtract),
            Tk.Star => (LevelProduct, BinaryOperator.Multiply),
            Tk.Slash => (LevelProduct, BinaryOperator.Divide),
            _ => (0, default(BinaryOperator)),
        };
        return level != 0;
    }

    /// <summary>A prefix <c>not</c> (only where the operator being read is looser than a comparison), a prefix minus, or a primary.</summary>
    private static TokenListParserResult<Tk, Expression> Operand(TokenList<Tk> input, int minimum, out int level)
    {
        level = LevelAtom;
        var first = input.ConsumeToken();
        if (first.HasValue)
        {
            var token = first.Value;
            if (token.Kind == Tk.Not && minimum <= LevelNot)
            {
                var operand = Climb(first.Remainder, LevelNot);
                if (!operand.HasValue)
                {
                    return operand;
                }

                level = LevelNot;
                return TokenListParserResult.Value<Tk, Expression>(new Unary(UnaryOperator.Not, operand.Value, SpanOf(token).To(operand.Value.Span)), input, operand.Remainder);
            }

            if (token.Kind == Tk.Minus)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
                var operand = Operand(first.Remainder, LevelAtom, out _);
                if (!operand.HasValue)
                {
                    return operand;
                }

                return TokenListParserResult.Value<Tk, Expression>(new Unary(UnaryOperator.Negate, operand.Value, SpanOf(token).To(operand.Value.Span)), input, operand.Remainder);
            }
        }

        return Primary(input);
    }

    private static TokenListParserResult<Tk, Expression> Primary(TokenList<Tk> input)
    {
        var first = input.ConsumeToken();
        if (!first.HasValue)
        {
            return TokenListParserResult.Empty<Tk, Expression>(input);
        }

        var token = first.Value;
        switch (token.Kind)
        {
            case Tk.Number:
                return Leaf(new NumberLiteral(double.Parse(TextOf(token), NumberStyles.Float, CultureInfo.InvariantCulture), SpanOf(token)), input, first.Remainder);
            case Tk.String:
                return Leaf(new StringLiteral(Unquote(token), SpanOf(token)), input, first.Remainder);
            case Tk.True or Tk.False:
                return Leaf(new BoolLiteral(token.Kind == Tk.True, SpanOf(token)), input, first.Remainder);
            case Tk.Missing:
                // The stand-in for a value that is missing; the tree is thrown away when there is one, so it only has to be a node.
                return Leaf(new NumberLiteral(0, SpanOf(token)), input, first.Remainder);
            case Tk.LeftParen:
                return Group(input, token, first.Remainder);
            case Tk.Identifier:
                return NameForm(input, token, first.Remainder);
            default:
                return TokenListParserResult.Empty<Tk, Expression>(input);
        }
    }

    private static TokenListParserResult<Tk, Expression> Leaf(Expression node, TokenList<Tk> input, TokenList<Tk> rest) => TokenListParserResult.Value(node, input, rest);

    private static TokenListParserResult<Tk, Expression> Group(TokenList<Tk> input, Token<Tk> open, TokenList<Tk> afterOpen)
    {
        var inner = Climb(afterOpen, 0);
        if (!inner.HasValue)
        {
            return inner;
        }

        var close = inner.Remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != Tk.RightParen)
        {
            return TokenListParserResult.Empty<Tk, Expression>(inner.Remainder);
        }

        return TokenListParserResult.Value(inner.Value with { Span = SpanOf(open).To(SpanOf(close.Value)) }, input, close.Remainder);
    }

    /// <summary>A name and what follows it: a call, <c>.name</c>, <c>tf("1h").name</c> (with or without arguments), or a subscript when it is a reference.</summary>
    private static TokenListParserResult<Tk, Expression> NameForm(TokenList<Tk> input, Token<Tk> nameToken, TokenList<Tk> afterName)
    {
        var name = new Identifier(TextOf(nameToken), SpanOf(nameToken));
        var next = afterName.ConsumeToken();
        var nextKind = next.HasValue ? next.Value.Kind : Tk.None;
        if (nextKind == Tk.LeftParen && name.Text == "tf")
        {
            var tail = QualifierTail.TryParse(afterName);
            if (!tail.HasValue)
            {
                return TokenListParserResult.CastEmpty<Tk, (StringLiteral, Identifier), Expression>(tail);
            }

            var (frame, member) = tail.Value;
            if (tail.Remainder.ConsumeToken() is { HasValue: true, Value.Kind: Tk.LeftParen })
            {
                return ParseCall(input, member, name.Span, frame, tail.Remainder);
            }

            return Subscripted(new Reference(member, name.Span.To(member.Span), null, frame), input, tail.Remainder);
        }

        if (nextKind == Tk.LeftParen)
        {
            return ParseCall(input, name, name.Span, null, afterName);
        }

        if (nextKind == Tk.Dot)
        {
            var member = Name.TryParse(next.Remainder);
            if (!member.HasValue)
            {
                return TokenListParserResult.CastEmpty<Tk, Identifier, Expression>(member);
            }

            return Subscripted(new Reference(member.Value, name.Span.To(member.Value.Span), name), input, member.Remainder);
        }

        return Subscripted(new Reference(name, name.Span), input, afterName);
    }

    /// <summary>A reference, followed by a subscript if there is one. A subscript is only read after a reference; after anything else the <c>[</c> is left for the caller to reject.</summary>
    private static TokenListParserResult<Tk, Expression> Subscripted(Reference reference, TokenList<Tk> input, TokenList<Tk> rest)
    {
        if (rest.ConsumeToken() is not { HasValue: true, Value.Kind: Tk.LeftBracket })
        {
            return TokenListParserResult.Value<Tk, Expression>(reference, input, rest);
        }

        var tail = SubscriptTail.TryParse(rest);
        if (!tail.HasValue)
        {
            return TokenListParserResult.CastEmpty<Tk, (int, SourceSpan, SourceSpan), Expression>(tail);
        }

        var (lag, lagSpan, end) = tail.Value;
        return TokenListParserResult.Value<Tk, Expression>(new Subscript(reference, lag, lagSpan, reference.Span.To(end)), input, tail.Remainder);
    }

    /// <summary><c>(</c> arguments separated by commas <c>)</c>, where an argument is <c>name=name</c> or an expression.</summary>
    private static TokenListParserResult<Tk, Expression> ParseCall(TokenList<Tk> input, Identifier name, SourceSpan start, StringLiteral? timeframe, TokenList<Tk> atOpen)
    {
        var open = atOpen.ConsumeToken();
        var rest = open.Remainder;
        var arguments = ImmutableArray.CreateBuilder<Argument>();
        var look = rest.ConsumeToken();
        if (!look.HasValue || look.Value.Kind != Tk.RightParen)
        {
            while (true)
            {
                var argument = ParseArgument(rest);
                if (!argument.HasValue)
                {
                    return TokenListParserResult.CastEmpty<Tk, Argument, Expression>(argument);
                }

                arguments.Add(argument.Value);
                rest = argument.Remainder;
                var separator = rest.ConsumeToken();
                if (separator.HasValue && separator.Value.Kind == Tk.Comma)
                {
                    rest = separator.Remainder;
                    continue;
                }

                break;
            }
        }

        var close = rest.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != Tk.RightParen)
        {
            return TokenListParserResult.Empty<Tk, Expression>(rest);
        }

        return TokenListParserResult.Value<Tk, Expression>(new Call(name, arguments.ToImmutable(), start.To(SpanOf(close.Value)), timeframe), input, close.Remainder);
    }

    /// <summary>A named argument when a name is followed by <c>=</c>, otherwise a positional one: decided by looking at two tokens, so a mistake after the <c>=</c> is reported where it is.</summary>
    private static TokenListParserResult<Tk, Argument> ParseArgument(TokenList<Tk> input)
    {
        var first = input.ConsumeToken();
        if (first.HasValue && first.Value.Kind is Tk.Identifier or Tk.Missing && first.Remainder.ConsumeToken() is { HasValue: true, Value.Kind: Tk.Assign })
        {
            return NamedArgumentParser.TryParse(input);
        }

        var value = Climb(input, 0);
        if (!value.HasValue)
        {
            return TokenListParserResult.CastEmpty<Tk, Expression, Argument>(value);
        }

        return TokenListParserResult.Value<Tk, Argument>(new PositionalArgument(value.Value, value.Value.Span), input, value.Remainder);
    }

    private static TokenListParser<Tk, (int Value, SourceSpan Span)> LagNumber()
    {
        var whole = Token.EqualTo(Tk.Number).Where(IsWhole, "a whole number").Select(t => (ToInt(t), SpanOf(t)))
            .Or(Token.EqualTo(Tk.Missing).Select(t => (0, SpanOf(t))));
        return (from minus in Token.EqualTo(Tk.Minus)
                from number in whole
                select (-number.Item1, SpanOf(minus).To(number.Item2)))
            .Or(whole);
    }

    private static string Unquote(Token<Tk> token)
    {
        var text = TextOf(token);
        return text.Substring(1, text.Length - 2);
    }

    private static bool IsWhole(Token<Tk> token)
    {
        foreach (var c in token.Span.AsReadOnlySpan())
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static int ToInt(Token<Tk> token) => int.TryParse(TextOf(token), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
}
