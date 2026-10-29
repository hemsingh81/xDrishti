namespace XDrishti.Domain.Planning;

/// <summary>
/// The grade of what the owner's fills did on a ticket (P7-14, ADR 0093), stored beside the plan-price grade. A realised result, never a simulation: the label is always <see cref="Basis"/>.
/// </summary>
/// <param name="Status"><c>graded</c> (R measured against the plan's level) or <c>uncertain</c> (a pre-existing position or an oversized exit: no R), or <c>no_plan_levels</c> (the round trip is real but the plan grade has no entry or stop to measure it against; R is then empty).</param>
/// <param name="Qty">Shares entered and left.</param>
/// <param name="EntryPrice">Volume-weighted entry price of the fills.</param>
/// <param name="ExitPrice">Volume-weighted exit price of the fills.</param>
/// <param name="PlanEntryPrice">The plan grade's simulated entry; null when none.</param>
/// <param name="PlanStopPrice">The plan grade's simulated initial stop; null when none.</param>
/// <param name="PlanR">The plan grade's R beside it; null when the ticket was not graded as a trade.</param>
/// <param name="SlippagePrice">Entry price paid minus the plan entry, signed so that positive is worse for the owner; null without a plan entry.</param>
/// <param name="SlippageR">The same in R (divided by the planned risk per share); null without plan levels.</param>
/// <param name="GrossPnl">Realised profit before charges, rupees.</param>
/// <param name="Charges">The charges the sync recorded for the fills used, rupees.</param>
/// <param name="R">Realised result in R after the recorded charges; null without plan levels.</param>
/// <param name="Detail">Why, in a sentence.</param>
public sealed record FillGrade(
    string Status,
    long Qty,
    double EntryPrice,
    double ExitPrice,
    double? PlanEntryPrice,
    double? PlanStopPrice,
    double? PlanR,
    double? SlippagePrice,
    double? SlippageR,
    decimal GrossPnl,
    decimal Charges,
    double? R,
    string Detail)
{
    /// <summary>The label every fill grade carries: these are the owner's executions, not a replay.</summary>
    public const string Basis = "realised fills, not simulated";

    /// <summary>The doubt written when a live round trip has a fill whose charges were not recorded: a net R without them would read better than it was, so the rupee result is stored and no R (the sentence is a marker, see <c>IsChargesDoubt</c>).</summary>
    public const string ChargesNotRecorded = "uncertain: the charges of an intraday live fill are not recorded";

    /// <summary>True when the grade is uncertain only because charges were not recorded (it says nothing about whether the position was the ticket's own).</summary>
    public bool IsChargesDoubt => Status == Uncertain && Detail.Contains(ChargesNotRecorded, StringComparison.Ordinal);

    /// <summary>R measured against the plan.</summary>
    public const string Graded = "graded";

    /// <summary>The round trip may not be the ticket's alone (shares held before it, or an exit fill larger than the entry): the rupee result is stored, no R.</summary>
    public const string Uncertain = "uncertain";

    /// <summary>A round trip without plan levels to measure it against.</summary>
    public const string NoPlanLevels = "no_plan_levels";
}
