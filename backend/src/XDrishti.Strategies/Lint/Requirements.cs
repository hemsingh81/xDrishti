using XDrishti.Indicators;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// <c>needs_context</c> and <c>needs_expiry_days</c> (information): the catalog says which features need a market context or the expiry dates (<see cref="IndicatorInfo.RequiresContext"/>,
/// <see cref="IndicatorInfo.RequiresExpiryDays"/>); the condition passes that on so that the caller creates the frame with them. Not a fault. One note for each, at the first feature that needs it.
/// </summary>
internal static class Requirements
{
    /// <summary>The flags for the plan, and the notes when no error was found so far (a condition that cannot be used needs nothing).</summary>
    public static (bool NeedsContext, bool NeedsExpiryDays) Apply(IReadOnlyList<BoundFeature> leaves, BindingScope scope)
    {
        var context = false;
        var expiryDays = false;
        var note = scope.Problems.All(p => p.Severity != ProblemSeverity.Error);
        foreach (var leaf in leaves)
        {
            if (!IndicatorCatalog.TryGet(leaf.Spec.Name, out var info))
            {
                continue;
            }

            if (info.RequiresContext && !context)
            {
                context = true;
                if (note)
                {
                    scope.Information(
                        LintCodes.NeedsContext,
                        "`" + leaf.Spec.Name + "` reads the daily market context: create the frame with a MarketContext (FrameOptions.Context).",
                        leaf.Span);
                }
            }

            if (info.RequiresExpiryDays && !expiryDays)
            {
                expiryDays = true;
                if (note)
                {
                    scope.Information(
                        LintCodes.NeedsExpiryDays,
                        "`" + leaf.Spec.Name + "` reads the expiry dates: create the frame with the expiry dates (FrameOptions.ExpiryDays; an empty set is allowed).",
                        leaf.Span);
                }
            }
        }

        return (context, expiryDays);
    }
}
