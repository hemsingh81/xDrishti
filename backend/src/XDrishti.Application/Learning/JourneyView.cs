using XDrishti.Domain.Planning;

namespace XDrishti.Application.Learning;

/// <summary>One ISO week of graded plan tickets.</summary>
/// <param name="IsoWeek">The week.</param>
/// <param name="Start">The Monday.</param>
/// <param name="Graded">Graded tickets.</param>
/// <param name="MeanR">Mean R; null when the week is insufficient.</param>
/// <param name="TotalR">Sum of R.</param>
/// <param name="State">ok or insufficient.</param>
public sealed record JourneyWeekView(string IsoWeek, DateOnly Start, int Graded, double? MeanR, double TotalR, string State)
{
    internal static JourneyWeekView Of(JourneyWeek w) => new(w.IsoWeek, w.Start, w.Graded, w.MeanR, w.TotalR, w.State);
}

/// <summary>The plan-price line: grades by week.</summary>
/// <param name="State">ok, or empty when no ticket is graded.</param>
/// <param name="Weeks">Oldest first.</param>
/// <param name="TotalGraded">Graded tickets over all weeks.</param>
/// <param name="Basis">Always "simulated on plan prices".</param>
/// <param name="Note">Truncation or other caveat, or null.</param>
public sealed record JourneyPlanPriceView(string State, IReadOnlyList<JourneyWeekView> Weeks, int TotalGraded, string Basis, string? Note);

/// <summary>The live line, which is never evaluable until real trades exist.</summary>
/// <param name="State">Always not_evaluable.</param>
/// <param name="Reason">Why.</param>
/// <param name="N">Always 0.</param>
public sealed record JourneyLiveView(string State, string Reason, int N);

/// <summary>What the numbers on the Journey rest on.</summary>
/// <param name="Basis">gross, costed_unconfirmed or net_confirmed; null with no counterfactual.</param>
/// <param name="Population">The run keys behind the counterfactual.</param>
/// <param name="From">The cut day.</param>
/// <param name="To">The last day of the data.</param>
/// <param name="N">Trades scored.</param>
/// <param name="Limits">The report's limits.</param>
public sealed record JourneyEvidenceView(string? Basis, IReadOnlyList<string> Population, DateOnly? From, DateOnly? To, int N, IReadOnlyList<string> Limits);

/// <summary>The Journey (spec P8-12).</summary>
/// <param name="Counterfactual">The newest stored counterfactual's header, or null.</param>
/// <param name="PlanPrice">Plan-price grades by week.</param>
/// <param name="Live">The live line.</param>
/// <param name="Evidence">Basis, population, range, n and limits.</param>
public sealed record JourneyView(CounterfactualHeaderView? Counterfactual, JourneyPlanPriceView PlanPrice, JourneyLiveView Live, JourneyEvidenceView Evidence);
