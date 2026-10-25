namespace XDrishti.Domain.Learning;

/// <summary>One window's input to the cumulative uplift: the rows scored and the take-R per candidate of each side.</summary>
/// <param name="Rows">Rows scored by both sides.</param>
/// <param name="FrozenTakeR">Frozen side, R per candidate of taking what it scores at or above its base rate.</param>
/// <param name="RetrainedTakeR">Retrained side, the same.</param>
/// <param name="Informative">The retrained model saw labels the frozen one did not; only these windows count.</param>
public readonly record struct UpliftWindow(int Rows, double FrozenTakeR, double RetrainedTakeR, bool Informative);

/// <summary>One window's uplift in R and the running total.</summary>
/// <param name="Uplift">Rows times (retrained minus frozen take-R); null for a window with the same model on both sides (it is not judged and adds nothing).</param>
/// <param name="Cumulative">The sum of the uplifts up to and including this window.</param>
public readonly record struct UpliftPoint(double? Uplift, double Cumulative);

/// <summary>
/// The cumulative take-R uplift of continued learning (spec P8-12, decision 6): <c>sum of rows x (retrained take-R per candidate - frozen take-R per candidate)</c> over the informative windows. The take rule is the evaluator's, not a tuned threshold; this is a convention in R, not a strategy.
/// </summary>
public static class CounterfactualUplift
{
    /// <summary>The uplift of each window and the running total, in window order. Identical sides give zero; a window never changes another's figure.</summary>
    public static IReadOnlyList<UpliftPoint> Compute(IReadOnlyList<UpliftWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var points = new List<UpliftPoint>(windows.Count);
        var total = 0.0;
        foreach (var w in windows)
        {
            if (!w.Informative)
            {
                points.Add(new UpliftPoint(null, total));
                continue;
            }

            var uplift = w.Rows * (w.RetrainedTakeR - w.FrozenTakeR);
            total += uplift;
            points.Add(new UpliftPoint(uplift, total));
        }

        return points;
    }
}
