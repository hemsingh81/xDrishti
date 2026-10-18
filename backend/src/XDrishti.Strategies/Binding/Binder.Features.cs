using System.Collections.Immutable;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// A catalog feature: its timeframe, its arguments mapped to the catalog's parameters (positionally, defaults for the rest, the named <c>source=</c>), the canonical <see cref="FeatureSpec"/> and the catalog's own
/// validation of it. The ranges, the whole-number rule and the combinations (<c>fast</c> below <c>slow</c>, an opening range of whole bars) are not copied here: the catalog is asked, so there is one rule.
/// </summary>
internal sealed partial class Binder
{
    private const int MaxParameters = 4;

    private IBoundExpression BindFeature(IndicatorInfo info, Identifier name, ImmutableArray<Argument> arguments, StringLiteral? timeframeText, SourceSpan span, bool isCall)
    {
        if (!isCall && info.Parameters.Count > 0)
        {
            scope.Error(
                LintCodes.BadArguments,
                scope.Quoted(span) + " has parameters, so it is written with parentheses: " + info.Name + "() uses the defaults. " + Describe.Usage(info),
                span,
                info.Name + "()");
            return new BoundInvalid(span);
        }

        var valid = true;
        var timeframe = info.DateDriven ? Timeframe.M1 : scope.EvaluationTimeframe;
        if (timeframeText is not null)
        {
            if (Timeframes.TryParse(timeframeText.Value, out var written))
            {
                timeframe = written;
            }
            else
            {
                ReportBadTimeframe(timeframeText);
                valid = false;
            }
        }

        var values = new double[MaxParameters];
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            values[i] = info.Parameters[i].Default;
        }

        var argumentSpans = new SourceSpan?[MaxParameters];
        var source = info.UsesSource ? info.DefaultSource : PriceSource.Close;
        SourceSpan? sourceSpan = null;
        var positional = 0;
        SourceSpan? firstExtra = null;
        SourceSpan? lastExtra = null;
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case PositionalArgument value when positional < info.Parameters.Count:
                    if (TryConstantArgument(info, positional, value.Value, out var number))
                    {
                        values[positional] = number;
                        argumentSpans[positional] = value.Span;
                    }
                    else
                    {
                        valid = false;
                    }

                    positional++;
                    break;
                case PositionalArgument extra:
                    Bind(extra.Value);
                    firstExtra ??= extra.Span;
                    lastExtra = extra.Span;
                    positional++;
                    break;
                case NamedArgument named:
                    valid &= BindSource(info, named, ref source, ref sourceSpan);
                    break;
            }
        }

        var tooMany = false;
        if (firstExtra is { } from && lastExtra is { } to)
        {
            scope.Error(
                LintCodes.BadArguments,
                "`" + info.Name + "` takes " + (info.Parameters.Count == 0 ? "no arguments" : "at most " + Count(info.Parameters.Count, "argument")) + ", but " + Given(positional) + ". " + Describe.Usage(info),
                from.To(to));
            tooMany = true;
        }

        if (!valid)
        {
            return new BoundInvalid(span);
        }

        // the arguments that are there are numbers and the timeframe is known, so the catalog can say more about them even when there are too many
        var spec = new FeatureSpec(info.Name, timeframe, source, values[0], values[1], values[2], values[3]);
        var catalogOk = CheckWithCatalog(info, spec, timeframeText, span, argumentSpans, sourceSpan);
        if (!catalogOk || tooMany)
        {
            return new BoundInvalid(span);
        }

        if (timeframeText is not null && timeframe == scope.EvaluationTimeframe)
        {
            // the canonical text of the feature without the qualifier (the source text would carry the brackets of a parenthesised feature)
            var plain = ExpressionPrinter.Print(isCall ? new Call(name, arguments, span) : new Reference(name, span));
            scope.Warning(
                LintCodes.RedundantTimeframe,
                "`tf(\"" + timeframeText.Value + "\")` is redundant: the strategy already runs on " + timeframe.ToCode() + ". Write `" + plain + "`.",
                span,
                plain);
        }

        return new BoundFeature(spec, 0, info.Name == "regime" ? BoundType.Regime : BoundType.Number, !isCall, span);
    }

    /// <summary>A positional argument of a feature: a number known when the condition is compiled (a literal, a strategy parameter, a constant, or arithmetic on them).</summary>
    private bool TryConstantArgument(IndicatorInfo info, int index, Expression value, out double number)
    {
        number = 0;
        var bound = Bind(value);
        if (bound.Type == BoundType.Invalid)
        {
            return false;
        }

        var parameter = info.Parameters[index];
        var role = "Argument " + (index + 1) + " (`" + parameter.Name + "`) of `" + info.Name + "` ";
        if (bound.Type != BoundType.Number)
        {
            scope.Error(
                LintCodes.BadArguments,
                role + "must be a number, but " + scope.Quoted(bound.Span) + " is " + Describe.Type(bound.Type) + ". " + Describe.Usage(info),
                bound.Span);
            return false;
        }

        if (!ConstantFolder.TryFold(bound, out number))
        {
            scope.Error(
                LintCodes.BadArguments,
                role + "must be a number known when the condition is compiled (a number or a strategy parameter), but " + scope.Quoted(bound.Span) + " changes from bar to bar. " + Describe.Usage(info),
                bound.Span);
            return false;
        }

        if (!double.IsFinite(number))
        {
            scope.Error(LintCodes.BadArguments, role + "must be a finite number, but " + scope.Quoted(bound.Span) + " is " + Describe.Number(number) + ". " + Describe.Usage(info), bound.Span);
            return false;
        }

        return true;
    }

    /// <summary>The named argument <c>source=close|open|high|low</c> of a feature that reads a price.</summary>
    private bool BindSource(IndicatorInfo info, NamedArgument named, ref PriceSource source, ref SourceSpan? sourceSpan)
    {
        if (named.Name.Text != Vocabulary.SourceArgument)
        {
            scope.Error(
                LintCodes.BadArguments,
                "`" + info.Name + "` has no argument named " + scope.Quoted(named.Name.Span) + "; " + (info.UsesSource ? "the only named argument is `source`" : "it takes no named argument") + ". " + Describe.Usage(info),
                named.Span);
            return false;
        }

        if (!info.UsesSource)
        {
            scope.Error(LintCodes.BadArguments, "`" + info.Name + "` reads the whole bar, so it has no `source`. " + Describe.Usage(info), named.Span);
            return false;
        }

        if (sourceSpan is not null)
        {
            scope.Error(LintCodes.BadArguments, "`source` is given twice.", named.Span);
            return false;
        }

        if (!Vocabulary.TryPriceSource(named.Value.Text, out var chosen))
        {
            var close = Suggestions.Closest(named.Value.Text, Vocabulary.PriceSourceNames);
            scope.Error(
                LintCodes.BadArguments,
                scope.Quoted(named.Value.Span) + " is not a price source: `source` is close, open, high or low." + (close.Count > 0 ? " Did you mean `" + close[0] + "`?" : string.Empty),
                named.Value.Span,
                close.Count > 0 ? close[0] : null);
            return false;
        }

        source = chosen;
        sourceSpan = named.Span;
        return true;
    }

    /// <summary>Asks the catalog whether the spec is valid and turns its answers into problems at the argument, the timeframe or the feature they are about. True when the spec is valid.</summary>
    private bool CheckWithCatalog(IndicatorInfo info, FeatureSpec spec, StringLiteral? timeframeText, SourceSpan span, SourceSpan?[] argumentSpans, SourceSpan? sourceSpan)
    {
        var problems = scope.CatalogAnswer(spec);
        foreach (var problem in problems)
        {
            var field = CatalogCheck.FieldOf(problem);
            if (field == "timeframe")
            {
                scope.Error(
                    LintCodes.UnsupportedTimeframe,
                    problem.Message + " `" + info.Name + "` runs on " + Describe.SupportedTimeframes(info) + (timeframeText is null ? " (the strategy runs on " + spec.Timeframe.ToCode() + "; write tf(\"…\") to read it on another)." : "."),
                    timeframeText?.Span ?? span);
                continue;
            }

            var index = field.Length == 2 && field[0] == 'p' && field[1] is >= '1' and <= '4' ? field[1] - '1' : -1;
            var at = index >= 0 ? argumentSpans[index] ?? span : field == "source" ? sourceSpan ?? span : span;
            scope.Error(LintCodes.BadArguments, scope.Quoted(span) + ": " + problem.Message + " " + Describe.Usage(info), at);
        }

        return problems.Count == 0;
    }

    private void ReportBadTimeframe(StringLiteral timeframeText)
    {
        var codes = Timeframes.All.Select(t => t.ToCode()).ToList();
        var close = Suggestions.Closest(timeframeText.Value, codes, 1);
        scope.Error(
            LintCodes.UnsupportedTimeframe,
            scope.Quoted(timeframeText.Span) + " is not a timeframe: tf(\"…\") takes " + string.Join(", ", codes.Take(codes.Count - 1)) + " or " + codes[^1] + " (the case matters: 1m is a minute, 1M a month)."
                + (close.Count > 0 ? " Did you mean `\"" + close[0] + "\"`?" : string.Empty),
            timeframeText.Span,
            close.Count > 0 ? "\"" + close[0] + "\"" : null);
    }

    private static string Count(int count, string noun) => count + " " + noun + (count == 1 ? string.Empty : "s");

    private static string Given(int count) => count == 1 ? "1 was given" : count + " were given";
}
