using System.Globalization;

namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>The fit of one candidate to one basket budget, all in percent of capital.</summary>
/// <param name="Outcome">Fits, scaled or reserve.</param>
/// <param name="Ratio">The share of the candidate's quantity that may be taken, 0 to 1.</param>
/// <param name="Budget">The basket's risk budget, percent of capital.</param>
/// <param name="Used">Open risk the basket already uses.</param>
/// <param name="Room">What is left in the budget (not below 0).</param>
/// <param name="Candidate">The candidate's risk before the fit.</param>
/// <param name="UsesPctOfBudget">Share of the budget used after the candidate is taken at <paramref name="Ratio"/>; null for a reserve.</param>
public sealed record BudgetFit(BudgetFitOutcome Outcome, double Ratio, double Budget, double Used, double Room, double Candidate, double? UsesPctOfBudget)
{
    /// <summary>The reserve code.</summary>
    public const string ReserveCode = "basket_budget";

    /// <summary>The numbers in words, for the ticket and the reserve reason.</summary>
    public string Text => Outcome == BudgetFitOutcome.Reserve
        ? string.Create(CultureInfo.InvariantCulture, $"basket budget: room {Room:0.###} of {Budget:0.###} (open risk {Used:0.###}), candidate needs {Candidate:0.###}, below the floor")
        : string.Create(CultureInfo.InvariantCulture, $"uses {UsesPctOfBudget:0.#} % of basket budget");
}
