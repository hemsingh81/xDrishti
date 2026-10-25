using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Mirror;

/// <summary>
/// One rewrite of one condition: walks the tree, types every expression and builds the mirrored tree (spec P3-02e, <i>Contract</i>). A step that cannot be mirrored records one <see cref="Failure"/> (the first) and returns
/// null up the walk; a tree the language itself would refuse (an unknown name, a wrong number of arguments) sets <see cref="Unreadable"/> instead, since the compile of the original reports that. Not thread-safe: one instance per condition.
/// </summary>
internal sealed partial class MirrorRewriter(IReadOnlySet<string> parameters)
{
    private const string Advice = " Write a `sell:` block.";

    private const int MaxQuote = 120;

    /// <summary>A typed tree: the mirrored expression and its mirror type.</summary>
    private readonly record struct Typed(Expression Tree, MirrorKind Kind);

    /// <summary>The first problem found; null when none, or when the condition is <see cref="Unreadable"/>.</summary>
    public Problem? Failure { get; private set; }

    /// <summary>True when the condition does not mean anything the compiler accepts, so there is no mirror problem to report.</summary>
    public bool Unreadable { get; private set; }

    /// <summary>The mirrored condition, or null with <see cref="Failure"/> or <see cref="Unreadable"/> set.</summary>
    public Expression? Condition(Expression condition)
    {
        var result = Visit(condition);
        if (result is null)
        {
            return null;
        }

        return result.Value.Kind == MirrorKind.Bool ? result.Value.Tree : Bad();
    }

    private static bool IsTyped(MirrorKind kind) => kind is MirrorKind.Level or MirrorKind.Signed or MirrorKind.Osc;

    private static bool IsPlainLike(MirrorKind kind) => kind is MirrorKind.Scalar or MirrorKind.Plain;

    private static Identifier Id(string text, SourceSpan span) => new(text, span);

    private static string Quote(Expression node)
    {
        var text = ExpressionPrinter.Print(node);
        return "`" + (text.Length > MaxQuote ? text[..MaxQuote] + "..." : text) + "`";
    }

    private static string Describe(MirrorKind kind) => kind switch
    {
        MirrorKind.Level => "a price",
        MirrorKind.Signed => "a signed value",
        MirrorKind.Osc => "an oscillator",
        MirrorKind.Scalar => "a number",
        MirrorKind.Bool => "a condition",
        _ => "a plain quantity",
    };

    private Expression? Bad()
    {
        Unreadable = true;
        return null;
    }

    private Typed? BadTyped()
    {
        Unreadable = true;
        return null;
    }

    private Typed? Fail(Expression node, string code, string message)
    {
        if (Failure is null && !Unreadable)
        {
            Failure = new Problem(code, message + Advice, node.Span, "write a `sell:` block");
        }

        return null;
    }

    private Typed? UnknownName(Expression node)
    {
        Fail(node, MirrorCodes.UnknownName, Quote(node) + " is not a name the mirror table knows.");
        Unreadable = true;
        return null;
    }

    private Typed? ContextName(Expression node) =>
        Fail(node, MirrorCodes.ContextName, Quote(node) + " reads the market context or the calendar; whether the SELL wants the same filter or the opposite one is a trading judgment.");

    private Typed? Visit(Expression expression) => expression switch
    {
        NumberLiteral => new Typed(expression, MirrorKind.Scalar),
        StringLiteral => new Typed(expression, MirrorKind.Plain),
        BoolLiteral => new Typed(expression, MirrorKind.Bool),
        Reference reference => Atom(reference),
        Subscript subscript => Lagged(subscript),
        Call call => CallOf(call),
        Unary unary => UnaryOf(unary),
        Binary binary => BinaryOf(binary),
        _ => BadTyped(),
    };

    private Typed? Atom(Reference reference)
    {
        var name = reference.Name.Text;
        var scope = reference.Scope?.Text;
        if (scope is null && reference.Timeframe is null)
        {
            if (parameters.Contains(name))
            {
                return new Typed(reference, MirrorKind.Scalar);
            }

            if (name == Vocabulary.TimeKeyword)
            {
                return new Typed(reference, MirrorKind.Plain);
            }
        }

        var flat = scope is null ? name : scope + "_" + name;
        if (scope == "index" || MirrorTable.IsRefused(flat))
        {
            return ContextName(reference);
        }

        if (!MirrorTable.TryGet(flat, out var row))
        {
            return UnknownName(reference);
        }

        var mirrored = scope is not null && row.Mirrored.StartsWith(scope + "_", StringComparison.Ordinal) ? row.Mirrored[(scope.Length + 1)..] : row.Mirrored;
        return new Typed(reference with { Name = Id(mirrored, reference.Name.Span) }, row.Kind);
    }

    private Typed? Lagged(Subscript subscript)
    {
        var target = Atom(subscript.Target);
        return target is { Tree: Reference reference } typed ? new Typed(subscript with { Target = reference }, typed.Kind) : null;
    }

    private Typed? CallOf(Call call)
    {
        if (call.Timeframe is null && !parameters.Contains(call.Name.Text) && Vocabulary.TryBuiltin(call.Name.Text, out var builtin))
        {
            return BuiltinCall(call, builtin);
        }

        var name = call.Name.Text;
        if (MirrorTable.IsRefused(name))
        {
            return ContextName(call);
        }

        if (!MirrorTable.TryGet(name, out var row))
        {
            return UnknownName(call);
        }

        var arguments = call.Arguments.Select(a => a is NamedArgument { Name.Text: Vocabulary.SourceArgument } named ? named with { Value = Id(SwapSource(named.Value.Text), named.Value.Span) } : a);
        return new Typed(call with { Name = Id(row.Mirrored, call.Name.Span), Arguments = [.. arguments] }, row.Kind);
    }

    private static string SwapSource(string source) => source switch
    {
        "high" => "low",
        "low" => "high",
        _ => source,
    };

    private Typed? UnaryOf(Unary unary)
    {
        var operand = Visit(unary.Operand);
        if (operand is not { } typed)
        {
            return null;
        }

        if (unary.Operator == UnaryOperator.Not)
        {
            return typed.Kind == MirrorKind.Bool ? new Typed(unary with { Operand = typed.Tree }, MirrorKind.Bool) : BadTyped();
        }

        return typed.Kind switch
        {
            MirrorKind.Scalar or MirrorKind.Plain or MirrorKind.Signed => new Typed(unary with { Operand = typed.Tree }, typed.Kind),
            MirrorKind.Bool => BadTyped(),
            _ => Fail(unary, MirrorCodes.NotMirrorable, Quote(unary) + " negates " + Describe(typed.Kind) + ", which has no mirror."),
        };
    }
}
