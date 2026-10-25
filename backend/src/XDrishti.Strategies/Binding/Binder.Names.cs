using System.Collections.Immutable;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// Names: what an identifier means. In this order: a strategy parameter (a number), then a builtin, a constant or <c>time</c>, then a catalog feature. A feature qualified with <c>tf("…")</c> or written with a scope
/// (<c>prev_day.high</c>) is always a catalog feature.
/// </summary>
internal sealed partial class Binder
{
    private IBoundExpression BindReference(Reference reference)
    {
        if (reference.Timeframe is not null)
        {
            return BindQualified(reference.Name, reference.Timeframe, [], false, reference.Span);
        }

        return reference.Scope is not null ? BindDotted(reference) : BindName(reference);
    }

    /// <summary>A bare name: a parameter, <c>time</c>, a constant, or a feature without parameters; a function or a feature with parameters needs its parentheses.</summary>
    private IBoundExpression BindName(Reference reference)
    {
        var name = reference.Name;
        if (ParameterValue(name, reference.Span) is { } parameter)
        {
            return parameter;
        }

        if (name.Text == Vocabulary.TimeKeyword)
        {
            return new BoundTimeKeyword(reference.Span);
        }

        if (Vocabulary.TryConstant(name.Text, out var constant))
        {
            return new BoundNumber(constant, reference.Span);
        }

        if (Vocabulary.TryBuiltin(name.Text, out var builtin))
        {
            scope.Error(
                LintCodes.BadArguments,
                scope.Quoted(name.Span) + " is a function: call it with parentheses and arguments, like " + BuiltinExample(builtin) + ".",
                reference.Span);
            return new BoundInvalid(reference.Span);
        }

        if (name.Text == Vocabulary.TimeframeQualifier)
        {
            scope.Error(LintCodes.BadArguments, "`tf` qualifies a feature with a timeframe: write tf(\"1h\").close or tf(\"1h\").ema(20).", reference.Span);
            return new BoundInvalid(reference.Span);
        }

        if (IndicatorCatalog.TryGet(name.Text, out var info))
        {
            return BindFeature(info, name, [], null, reference.Span, false);
        }

        return UnknownName(name, false);
    }

    /// <summary><c>prev_day.high</c>, <c>today.open</c>, <c>index.trend</c>: the DSL spellings of the flat catalog names. Every other pair is an error.</summary>
    private IBoundExpression BindDotted(Reference reference)
    {
        var scopeName = reference.Scope!.Text;
        var member = reference.Name.Text;
        if (!Vocabulary.TryScope(scopeName, out var members))
        {
            var close = Suggestions.Closest(scopeName, Vocabulary.ScopeNames);
            scope.Error(
                LintCodes.UnknownName,
                scope.Quoted(reference.Scope.Span) + " is not a scope; the dotted names are prev_day.open|high|low|close|volume, today.open|high|low and index.trend|adx."
                    + (close.Count > 0 ? " Did you mean " + Suggestions.Listed(close) + "?" : string.Empty),
                reference.Scope.Span,
                close.Count > 0 ? close[0] : null);
            return new BoundInvalid(reference.Span);
        }

        var flat = scopeName + "_" + member;
        if (!members.Contains(member) || !IndicatorCatalog.TryGet(flat, out var info))
        {
            var closeMembers = Suggestions.Closest(member, members);
            var suggestion = IndicatorCatalog.TryGet(flat, out _) ? flat : closeMembers.Count > 0 ? scopeName + "." + closeMembers[0] : null;
            scope.Error(
                LintCodes.UnknownName,
                scope.Quoted(reference.Span) + " is not a name: after `" + scopeName + ".` comes " + AnyOf(members) + "."
                    + (suggestion is null ? string.Empty : " Did you mean `" + suggestion + "`?"),
                reference.Span,
                suggestion);
            return new BoundInvalid(reference.Span);
        }

        return BindFeature(info, reference.Name, [], null, reference.Span, false);
    }

    /// <summary><c>tf("1h").name</c> or <c>tf("1h").name(arguments)</c>: only a catalog feature can be qualified.</summary>
    private IBoundExpression BindQualified(Identifier name, StringLiteral timeframe, ImmutableArray<Argument> arguments, bool isCall, SourceSpan span)
    {
        if (IndicatorCatalog.TryGet(name.Text, out var info))
        {
            return BindFeature(info, name, arguments, timeframe, span, isCall);
        }

        var what = scope.Parameters.ContainsKey(name.Text) ? "a strategy parameter"
            : name.Text == Vocabulary.TimeKeyword ? "the keyword `time`"
            : Vocabulary.TryConstant(name.Text, out _) ? "a constant"
            : Vocabulary.TryBuiltin(name.Text, out _) ? "a function of the language"
            : null;
        if (what is not null)
        {
            scope.Error(
                LintCodes.UnknownName,
                scope.Quoted(name.Span) + " is " + what + ", not a feature, so it has no timeframe: only a feature is written tf(\"…\").name. Functions take the features as arguments, like crosses_above(tf(\"1h\").close, vwap).",
                name.Span);
        }
        else
        {
            ReportUnknown(name, IndicatorCatalog.All.Select(i => i.Name), "a feature");
        }

        return new BoundInvalid(span, BindForDiagnostics(arguments));
    }

    private IBoundExpression BindCall(Call call)
    {
        var name = call.Name;
        if (call.Timeframe is not null)
        {
            return BindQualified(name, call.Timeframe, call.Arguments, true, call.Span);
        }

        if (scope.Parameters.ContainsKey(name.Text))
        {
            WarnIfShadowing(name);
            scope.Error(LintCodes.BadArguments, scope.Quoted(name.Span) + " is a strategy parameter (a number), not a function; it cannot be called.", name.Span);
            return new BoundInvalid(call.Span, BindForDiagnostics(call.Arguments));
        }

        if (Vocabulary.TryBuiltin(name.Text, out var builtin))
        {
            return BindBuiltin(builtin, call);
        }

        if (name.Text == Vocabulary.TimeKeyword || Vocabulary.TryConstant(name.Text, out _) || name.Text == Vocabulary.TimeframeQualifier)
        {
            scope.Error(LintCodes.BadArguments, scope.Quoted(name.Span) + " is not a function; write it without parentheses.", name.Span);
            return new BoundInvalid(call.Span, BindForDiagnostics(call.Arguments));
        }

        if (IndicatorCatalog.TryGet(name.Text, out var info))
        {
            return BindFeature(info, name, call.Arguments, null, call.Span, true);
        }

        return UnknownName(name, true, call.Span, BindForDiagnostics(call.Arguments));
    }

    /// <summary>
    /// <c>close[1]</c> or <c>highest(20)[1]</c>: the feature some closed bars of its own timeframe ago, 1 or more. The target is bound first, so that its own problems come first; a lag that is not usable is reported after them (see
    /// <see cref="ReportUnusableLag"/>).
    /// </summary>
    private IBoundExpression BindSubscript(Subscript subscript)
    {
        var target = Bind(subscript.Target);
        if (target.Type == BoundType.Invalid)
        {
            ReportUnusableLag(subscript, false);
            return new BoundInvalid(subscript.Span);
        }

        if (target is not BoundFeature feature)
        {
            var name = subscript.Target is Reference reference ? reference.Name.Text : string.Empty;
            var kind = subscript.Target is Call ? "a function of the language"
                : scope.Parameters.ContainsKey(name) ? "a strategy parameter" : Vocabulary.TryConstant(name, out _) ? "a constant" : "the time of day";
            scope.Error(
                LintCodes.LagOnNonFeature,
                "A subscript reads a feature some closed bars ago, but " + scope.Quoted(subscript.Target.Span) + " is " + kind
                    + ", not a feature. Use crosses_above, crosses_below, rising or falling to compare with an earlier value, or write the feature twice.",
                subscript.Target.Span);
            ReportUnusableLag(subscript, true);
            return new BoundInvalid(subscript.Span, [target]);
        }

        ReportUnusableLag(subscript, true);
        return subscript.Lag > 0 ? Lagged(feature, subscript) : new BoundInvalid(subscript.Span, [feature]);
    }

    /// <summary>
    /// Reports a subscript that is not a whole number from 1 up. A subscript of zero (<c>close[0]</c>, <c>close[-0]</c>, <c>close[00]</c>) is the target itself: the span is the whole subscript and the suggestion is the target as
    /// written, so that replacing the span with the suggestion is the fix, and the target gives no suggestion when it could not be bound (<c>closee[0]</c> has the problem of <c>closee</c> to fix first, and its text
    /// would overlap that problem). A negative lag would read the future: the span is the number, and there is nothing to suggest.
    /// </summary>
    private void ReportUnusableLag(Subscript subscript, bool targetBound)
    {
        if (subscript.Lag > 0)
        {
            return;
        }

        var zero = subscript.Lag == 0;
        scope.Error(
            LintCodes.NegativeOrZeroLag,
            zero
                ? "A subscript counts closed bars back and must be a whole number from 1 up; " + scope.Quoted(subscript.Span) + " is just " + scope.Quoted(subscript.Target.Span) + "."
                : "A subscript counts closed bars back and must be a whole number from 1 up; " + scope.Quoted(subscript.LagSpan) + " would read the future.",
            zero ? subscript.Span : subscript.LagSpan,
            zero && targetBound ? scope.Text(subscript.Target.Span) : null);
    }

    private static BoundFeature Lagged(BoundFeature feature, Subscript subscript) => feature with { Lag = subscript.Lag, Span = subscript.Span };

    /// <summary>A strategy parameter as a number, or null when the name is not a parameter. A parameter that is not a finite number is an error.</summary>
    private IBoundExpression? ParameterValue(Identifier name, SourceSpan span)
    {
        if (!scope.Parameters.TryGetValue(name.Text, out var value))
        {
            return null;
        }

        WarnIfShadowing(name);
        if (double.IsFinite(value))
        {
            return new BoundNumber(value, span);
        }

        scope.Error(
            LintCodes.TypeMismatch,
            "The strategy parameter " + scope.Quoted(name.Span) + " is " + Describe.Number(value) + ", not a finite number; parameters are numbers.",
            span);
        return new BoundInvalid(span);
    }

    private void WarnIfShadowing(Identifier name)
    {
        if (!Vocabulary.IsLanguageName(name.Text) || !scope.FirstShadowOf(name.Text))
        {
            return;
        }

        var hides = name.Text == Vocabulary.TimeKeyword ? "the keyword `time`"
            : Vocabulary.TryConstant(name.Text, out _) ? "the constant `" + name.Text + "`"
            : Vocabulary.TryBuiltin(name.Text, out _) ? "the function `" + name.Text + "`"
            : "the feature `" + name.Text + "`";
        scope.Warning(
            LintCodes.ShadowedName,
            "The strategy parameter " + scope.Quoted(name.Span) + " has the name of " + hides + " and wins over it: here `" + name.Text + "` is the parameter's value. Rename the parameter to read " + hides + ".",
            name.Span);
    }

    /// <summary>Binds the positional arguments of a call that cannot be bound as a whole, so that the problems inside them are reported too; they stay in the tree as parts of the part that failed.</summary>
    private List<IBoundExpression> BindForDiagnostics(ImmutableArray<Argument> arguments)
    {
        var bound = new List<IBoundExpression>();
        foreach (var argument in arguments)
        {
            if (argument is PositionalArgument positional)
            {
                bound.Add(Bind(positional.Value));
            }
        }

        return bound;
    }

    private BoundInvalid UnknownName(Identifier name, bool callable, SourceSpan? whole = null, IReadOnlyList<IBoundExpression>? parts = null)
    {
        var vocabulary = IndicatorCatalog.All.Select(i => i.Name).Concat(Vocabulary.BuiltinNames).ToList();
        if (!callable)
        {
            vocabulary.AddRange(scope.Parameters.Keys);
            vocabulary.AddRange(Vocabulary.ConstantNames);
            vocabulary.AddRange([Vocabulary.TimeKeyword, "true", "false"]);
        }

        ReportUnknown(name, vocabulary, callable ? "a function or a feature" : "a parameter, a function, a constant or a feature");
        return new BoundInvalid(whole ?? name.Span, parts);
    }

    private void ReportUnknown(Identifier name, IEnumerable<string> vocabulary, string what)
    {
        var close = Suggestions.Closest(name.Text, vocabulary);
        scope.Error(
            LintCodes.UnknownName,
            scope.Quoted(name.Span) + " is not " + what + "." + (close.Count > 0 ? " Did you mean " + Suggestions.Listed(close) + "?" : string.Empty),
            name.Span,
            close.Count > 0 ? close[0] : null);
    }

    private static string AnyOf(IReadOnlyList<string> names) => string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];

    private static string BuiltinExample(Builtin builtin) => Vocabulary.NameOf(builtin) + "(" + string.Join(", ", Vocabulary.ParameterNames(builtin)) + ")";
}
