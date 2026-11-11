namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>The fit of a candidate to the room left in its basket's share of the day's open risk (spec BK-10, criterion 5). Pure and down only: the ratio is never above 1.</summary>
public static class BasketRiskBudget
{
    /// <summary>
    /// <c>room = budgetPct/100 * totalLimit - used</c>. A candidate whose risk is within the room is taken whole; otherwise it is scaled by <c>room / risk</c> if that is at least <paramref name="floor"/>, else it is a reserve.
    /// A candidate with no risk above 0 has nothing to fit and is taken whole.
    /// </summary>
    /// <param name="budgetPct">The basket's share of the total open risk limit, in percent (1 to 100).</param>
    /// <param name="totalLimit">The total open risk limit, percent of capital.</param>
    /// <param name="used">The basket's open risk (today's book plus earlier picks), percent of capital.</param>
    /// <param name="candidate">The candidate's risk, percent of capital.</param>
    /// <param name="floor">The lowest share of its quantity the candidate may be cut to.</param>
    public static BudgetFit Fit(double budgetPct, double totalLimit, double used, double candidate, double floor)
    {
        var budget = budgetPct / 100 * totalLimit;
        var room = Math.Max(0, budget - used);
        var low = double.IsFinite(floor) ? Math.Clamp(floor, SizingOptions.LowestFloor, 1.0) : SizingOptions.LowestFloor;
        if (candidate <= 0 || candidate <= room)
        {
            return new BudgetFit(BudgetFitOutcome.Fits, 1.0, budget, used, room, candidate, budget > 0 ? (used + Math.Max(0, candidate)) / budget * 100 : null);
        }

        var ratio = room / candidate;
        return ratio < low
            ? new BudgetFit(BudgetFitOutcome.Reserve, 0, budget, used, room, candidate, null)
            : new BudgetFit(BudgetFitOutcome.Scaled, ratio, budget, used, room, candidate, (used + (candidate * ratio)) / budget * 100);
    }
}
