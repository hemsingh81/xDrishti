namespace XDrishti.Domain.Planning;

/// <summary>
/// The result of the ticket builder for one instrument and one cell, as plain data (P7-04, ADR 0088). Computed from data up to the close of <see cref="Session"/> only. Levels are text rules with, where the data allows, a number
/// as of that close; a number labelled indicative is a reading of the close of D, not tomorrow's price.
/// </summary>
/// <param name="Verdict">Conditional, rejected or unavailable.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="StrategyVersionHash">The version it was built from.</param>
/// <param name="Session">The last closed session (D).</param>
/// <param name="Setup">Kleene value of the setup list (True when there is none).</param>
/// <param name="Trigger">Kleene value of the trigger list.</param>
/// <param name="Invalidate">Kleene value of the invalidate list (False when there is none).</param>
/// <param name="Overall">Kleene value of setup and trigger and not invalidate. True means the signal already fired at the close.</param>
/// <param name="Parts">Every condition with its classification.</param>
/// <param name="Reason">One line: why this verdict.</param>
/// <param name="TriggerText">What is left to check live.</param>
/// <param name="StopText">The stop rule and level.</param>
/// <param name="TargetText">The target rule and indicative level.</param>
/// <param name="ValidityText">When the ticket may be acted on.</param>
/// <param name="StopLevel">Stop price as of the close of D, when the stop reads closed daily data.</param>
/// <param name="IndicativeEntry">The close of D, the reference for the indicative levels.</param>
/// <param name="RewardRisk">Planned reward to risk of the first fixed-RR exit, when there is one.</param>
/// <param name="AverageDailyTurnover">Mean of close times volume over the last 20 sessions up to D, rupees; null when fewer than 20 sessions.</param>
/// <param name="Unavailable">Inputs that were not available; never a pass.</param>
/// <param name="TargetLevel">Target price as of the close of D, for a fixed reward-to-risk exit with a numeric stop (the same number the target text states); null otherwise (P7-19).</param>
/// <param name="StopAnchor">What <paramref name="StopLevel"/> is measured from; null when the stop has no number the plan may store (P7-19).</param>
public sealed record TicketDraft(
    TicketVerdict Verdict,
    string Symbol,
    string Side,
    string StrategyId,
    string StrategyVersionHash,
    DateOnly Session,
    SetupTruth Setup,
    SetupTruth Trigger,
    SetupTruth Invalidate,
    SetupTruth Overall,
    IReadOnlyList<TicketPart> Parts,
    string Reason,
    string TriggerText,
    string StopText,
    string TargetText,
    string ValidityText,
    double? StopLevel,
    double? IndicativeEntry,
    double? RewardRisk,
    double? AverageDailyTurnover,
    IReadOnlyList<string> Unavailable,
    double? TargetLevel = null,
    StopAnchor? StopAnchor = null);
