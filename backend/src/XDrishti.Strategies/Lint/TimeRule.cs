using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Lint;

/// <summary><c>time_on_daily</c>: <c>time</c> is the India clock time at which the evaluated bar closed, which a 1D, 1W or 1M bar does not have (it covers the whole session or more), so it is refused there.</summary>
internal static class TimeRule
{
    public static void Apply(IBoundExpression root, BindingScope scope)
    {
        if (scope.EvaluationTimeframe is not (Timeframe.D1 or Timeframe.W1 or Timeframe.Mo1))
        {
            return;
        }

        Visit(root, scope);
    }

    private static void Visit(IBoundExpression node, BindingScope scope)
    {
        // a comparison of time with a good text, or the keyword left in a part that could not be bound (a bad text, a number to compare with …)
        var keyword = node switch
        {
            BoundTimeCompare compare => compare.KeywordSpan,
            BoundTimeKeyword bare => bare.Span,
            _ => (SourceSpan?)null,
        };
        if (keyword is { } span)
        {
            scope.Error(
                LintCodes.TimeOnDaily,
                "`time` is the minute at which the evaluated bar closed, and a " + scope.EvaluationTimeframe.ToCode() + " bar has no time of day. Run the strategy on an intraday timeframe, or leave the time out.",
                span);
            return;
        }

        foreach (var child in BoundTree.Children(node))
        {
            Visit(child, scope);
        }
    }
}
