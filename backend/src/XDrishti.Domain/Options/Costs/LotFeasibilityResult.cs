namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// One lot against a cash trade's rupee risk: the verdict and its word, the risk of the cash trade (<c>qty x |entry - stop|</c>, computed whatever the verdict), the loss of one lot when it could be computed, and the label <c>lot: unconfirmed</c> when the
/// lot in force is not confirmed against a circular.
/// </summary>
public sealed record LotFeasibilityResult(FeasibilityVerdict Verdict, string Code, decimal RiskRupees, decimal? LossPerLotRupees, int? LotSize, string? Label);
