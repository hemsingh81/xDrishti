namespace XDrishti.Application.Reporting;

/// <summary>
/// What the run charged the trade. The figures are present only when the owner confirmed this very charge sheet (<c>confirmed</c>); otherwise only the sheet id is, and the screen says "costed on sheet X, not confirmed as yours" beside the gross figures.
/// </summary>
public sealed record ReplayCostingView(string SheetId, bool Confirmed, long? Quantity, decimal? TotalCharges, double? CostR, double? RNet);
