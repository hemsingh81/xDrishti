namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>What is known about the market before the decision date. A null figure means "not known", never zero (these feeds do not exist until P3-01d/e and P15).</summary>
/// <param name="AsOf">The date the figures describe (the VIX and breadth close; for the event flag, the date the calendar was read); before the decision date and not older than the maximum age.</param>
/// <param name="Vix">India VIX close.</param>
/// <param name="Breadth">Share (0 to 1) of tracked stocks above their 20-day average.</param>
/// <param name="HighImpactEventToday">True when a listed high-impact event (RBI policy, Budget) falls on the decision date itself (read from the calendar published ahead); null when the calendar is not known.</param>
public sealed record MarketGateInput(DateOnly AsOf, double? Vix, double? Breadth, bool? HighImpactEventToday);
