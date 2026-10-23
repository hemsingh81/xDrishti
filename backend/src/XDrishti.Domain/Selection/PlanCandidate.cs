using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Selection;

/// <summary>
/// One candidate for the plan, plain data (spec P7-01): a cell, the instrument it fires on, and the numbers known as of the decision date. A null number means "not known", never zero; the filters read it as unavailable.
/// </summary>
/// <param name="Symbol">The instrument. <c>*</c> stands for "the cell, no instrument yet" (the read-only verb).</param>
/// <param name="Cell">The cell the candidate comes from; its side is the candidate's side.</param>
/// <param name="State">The cell's state at the decision date; null when not known.</param>
/// <param name="Stats">The cell's statistics (all-time window), as of a date before the decision date; null when none.</param>
/// <param name="DecisionDate">The session the plan is for. Only data known before it may be used.</param>
/// <param name="PlannedRewardRisk">Reward to risk of the ticket before the nearest obstacle.</param>
/// <param name="CostR">Round-trip cost of the ticket in R.</param>
/// <param name="AverageDailyTurnover">Average daily turnover of the instrument in rupees.</param>
/// <param name="ExcludedByEvent">True when an event excludes the instrument.</param>
/// <param name="OnFnoBan">True when the instrument is on the F&amp;O ban list.</param>
/// <param name="UnderSurveillance">True when the instrument is under surveillance.</param>
/// <param name="RegimeFit">How well the cell suits the market regime, 0 to 1.</param>
/// <param name="CorrelationWithBook">Correlation with the open book, 0 to 1.</param>
/// <param name="EventRisk">Event risk, 0 to 1.</param>
/// <param name="AdvisoryPWin">The champion model's P(win) for this candidate; shown beside the cell figure and used for nothing else (P7-00 Q8).</param>
public sealed record PlanCandidate(
    string Symbol,
    CellId Cell,
    CellState? State,
    CellStatsRow? Stats,
    DateOnly DecisionDate,
    double? PlannedRewardRisk = null,
    double? CostR = null,
    double? AverageDailyTurnover = null,
    bool? ExcludedByEvent = null,
    bool? OnFnoBan = null,
    bool? UnderSurveillance = null,
    double? RegimeFit = null,
    double? CorrelationWithBook = null,
    double? EventRisk = null,
    double? AdvisoryPWin = null)
{
    /// <summary>The side of the cell.</summary>
    public string Side => Cell.Side;
}
