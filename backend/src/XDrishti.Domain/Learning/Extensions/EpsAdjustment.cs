using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// Putting the EPS of an earlier quarter on the share basis of a later date (P15-05, pure). A quarter's EPS is multiplied by the factor of every applied split or bonus whose ex-date lies after the day the quarter was <b>published</b> and at or before the date (the factor of an action
/// is the price factor for the bars before it, the same number that adjusts the price). A deliberate deviation from the spec's "after the period end" (ADR 0185): under Ind AS 33 a quarter published after an action already reports its EPS on the new share count, and adjusting it again
/// would count the action twice. An action after the date is not known then and is never applied. Also the price-window factor of <c>results_car3</c> (<c>after</c> is a session date there).
/// </summary>
public static class EpsAdjustment
{
    public static double Factor(IReadOnlyList<AppliedAction> actions, DateOnly after, DateOnly upTo)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var factor = 1.0;
        foreach (var action in actions)
        {
            if (action.ExDate > after && action.ExDate <= upTo)
            {
                factor *= (double)action.Factor;
            }
        }

        return factor;
    }

    /// <summary>The day after which an action is news to a quarter: the India date of its broadcast, or its period end when the fact carries no broadcast time.</summary>
    public static DateOnly SinceOf(DateTimeOffset? broadcast, DateOnly periodEnd) => broadcast is { } time ? IndiaTime.Today(time) : periodEnd;
}
