using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The one reading of the hold-out after a stored KEEP (P13-05): both models are fitted on all development rows (those whose label horizon ended before the first hold-out decision), their thresholds come from all development out-of-fold scores, and both score the hold-out rows. Confirmed
/// when the gain has the same sign as the development gain and B's log loss is not above A's; not evaluable under <c>MinHoldOutRows</c> rows. That it happens once is the store's rule (a unique index on the family and version key), not this function's.
/// </summary>
public static class ExtensionHoldOutCheck
{
    /// <summary>Reads the hold-out.</summary>
    /// <param name="development">The development rows (B's width), all dated before the hold-out.</param>
    /// <param name="holdOut">The hold-out rows (B's width).</param>
    /// <param name="baseWidth">The number of <c>tf-1</c> values.</param>
    /// <param name="group">The extension.</param>
    /// <param name="recipe">The recipe.</param>
    /// <param name="developmentDeltaR">The gain of the stored KEEP; its sign is what the hold-out must repeat.</param>
    /// <param name="sessions">The trading sessions.</param>
    /// <param name="options">The knobs.</param>
    public static Result<ExtensionHoldOutResult> Check(IReadOnlyList<ValidationRow> development, IReadOnlyList<ValidationRow> holdOut, int baseWidth, ExtensionGroup group, IScorerFactory recipe, double developmentDeltaR, IReadOnlyList<DateOnly> sessions, ExtensionComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(development);
        ArgumentNullException.ThrowIfNull(holdOut);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems().ToList();
        if (group.Problem() is { } groupProblem)
        {
            problems.Add(groupProblem);
        }

        if (baseWidth < 1 || !double.IsFinite(developmentDeltaR))
        {
            problems.Add("the base width must be at least 1 and the development gain finite");
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ExtensionHoldOutResult>(Error.Validation("extensions.options", string.Join("; ", problems)));
        }

        var width = baseWidth + group.Names.Count;
        var bad = ExtensionScoring.CheckRows(development, width, null) ?? ExtensionScoring.CheckRows(holdOut, width, null);
        if (bad is not null)
        {
            return Result.Failure<ExtensionHoldOutResult>(bad);
        }

        if (holdOut.Concat(development).GroupBy(r => r.Key, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            return Result.Failure<ExtensionHoldOutResult>(Error.Conflict("extensions.duplicate_key", "A development row and a hold-out row have the same key."));
        }

        if (development.Max(r => r.TradeDate) >= holdOut.Min(r => r.TradeDate))
        {
            return Result.Failure<ExtensionHoldOutResult>(Error.Validation("extensions.holdout_overlap", "Development rows must all be dated before the hold-out."));
        }

        if (holdOut.Count < options.MinHoldOutRows)
        {
            return Result.Success(new ExtensionHoldOutResult(ExtensionHoldOutStatus.NotEvaluable, holdOut.Count, null, null, null, null, null, "hold-out rows " + holdOut.Count + " of " + options.MinHoldOutRows));
        }

        var orderedDevelopment = development.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var orderedHoldOut = holdOut.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var sorted = sessions.Distinct().Order().ToArray();
        var viewA = ExtensionScoring.ViewA([.. orderedDevelopment, .. orderedHoldOut], baseWidth);
        var scoredResult = ExtensionScoring.Score(CalendarFolds.Cut(orderedDevelopment, sorted, options).Folds.Where(f => f.Usable), viewA, recipe);
        if (scoredResult.IsFailure)
        {
            return Result.Failure<ExtensionHoldOutResult>(scoredResult.Error);
        }

        var tA = ExtensionScoring.ThresholdFromAll(scoredResult.Value, 0, options);
        var tB = ExtensionScoring.ThresholdFromAll(scoredResult.Value, 1, options);
        if (tA is not { } ta || tB is not { } tb)
        {
            return Result.Success(new ExtensionHoldOutResult(ExtensionHoldOutStatus.NotEvaluable, holdOut.Count, null, null, null, tA, tB, "too few development out-of-fold scores to choose a threshold"));
        }

        var firstDecision = orderedHoldOut.Min(r => r.DecisionEpochSeconds);
        var train = orderedDevelopment.Where(r => r.ResolvedEpochSeconds + HoldOut.BarSeconds < firstDecision).ToArray();
        var fitted = ExtensionScoring.Score([new CalendarFold(orderedHoldOut[0].TradeDate, orderedHoldOut[^1].TradeDate, train, orderedHoldOut, null)], viewA, recipe);
        if (fitted.IsFailure)
        {
            return Result.Failure<ExtensionHoldOutResult>(fitted.Error);
        }

        var scored = fitted.Value[0];
        var sum = 0.0;
        for (var k = 0; k < orderedHoldOut.Length; k++)
        {
            sum += (((scored.B[k] >= tb) ? 1 : 0) - ((scored.A[k] >= ta) ? 1 : 0)) * orderedHoldOut[k].R;
        }

        var labels = orderedHoldOut.Select(r => r.Label).ToArray();
        var lossA = MetricsCalculator.Compute(scored.A, labels).LogLoss;
        var lossB = MetricsCalculator.Compute(scored.B, labels).LogLoss;
        var delta = sum / orderedHoldOut.Length;
        var confirmed = Math.Sign(delta) == Math.Sign(developmentDeltaR) && delta != 0 && lossB <= lossA;
        return Result.Success(new ExtensionHoldOutResult(confirmed ? ExtensionHoldOutStatus.Confirmed : ExtensionHoldOutStatus.NotConfirmed, orderedHoldOut.Length, delta, lossA, lossB, ta, tb, null));
    }
}
