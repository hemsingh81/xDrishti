namespace XDrishti.Domain.Reporting;

/// <summary>One approved ticket line (a ticket on one account) as plan versus actual reads it (P8-04). Plain numbers only: no store, no clock.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="StrategyId">The strategy that proposed it.</param>
/// <param name="ApprovedKind">owner or auto: who approved it; null when no approval row is stored.</param>
/// <param name="FillStatus">One of the <c>Status</c> constants of <see cref="PlanVsActual"/>: graded, uncertain, no_plan_levels, not_finished or none (no matched fill).</param>
/// <param name="PlanR">The fill grade's plan R (simulated on plan prices) beside the fills.</param>
/// <param name="SlippageR">Entry slippage in R, positive worse for the owner.</param>
/// <param name="ChargesRupees">Recorded charges in rupees.</param>
/// <param name="Quantity">Shares entered and left.</param>
/// <param name="PlanEntry">The plan entry price.</param>
/// <param name="PlanStop">The plan stop price.</param>
/// <param name="FillR">Realised R after the recorded charges.</param>
/// <param name="PlanGradeTriggered">The plan-price grade of the ticket triggered (it was graded as a trade).</param>
/// <param name="PlanGradeR">The plan-price grade's R; null when not graded as a trade.</param>
public sealed record PlanVsActualLine(
    long PlanId,
    long ItemId,
    string StrategyId,
    string? ApprovedKind,
    string FillStatus,
    double? PlanR,
    double? SlippageR,
    double? ChargesRupees,
    long? Quantity,
    double? PlanEntry,
    double? PlanStop,
    double? FillR,
    bool PlanGradeTriggered,
    double? PlanGradeR);
