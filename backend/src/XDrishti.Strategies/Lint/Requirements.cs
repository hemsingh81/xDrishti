using XDrishti.Indicators;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// <c>needs_expiry_days</c> (information): the catalog says which features need the expiry dates (<see cref="IndicatorInfo.RequiresExpiryDays"/>); the condition passes that on so that the caller creates the frame with them. Not a fault. One note for each, at the first feature that needs it.
/// </summary>
internal static class Requirements
{
    /// <summary>The flags for the plan, and the notes when no error was found so far (a condition that cannot be used needs nothing).</summary>
    public static bool Apply(IReadOnlyList<BoundFeature> leaves, BindingScope scope)
    {
        var expiryDays = false;
        var note = scope.Problems.All(p => p.Severity != ProblemSeverity.Error);
        foreach (var leaf in leaves)
        {
            if (!IndicatorCatalog.TryGet(leaf.Spec.Name, out var info))
            {
                continue;
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

        return expiryDays;
    }
}
