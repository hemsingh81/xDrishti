namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One lot of a holding as a rebuild from trades plans it (ADR 0023): open (no <see cref="ClosedOn"/>) or a closed piece with the real sale day and price.
/// <see cref="Holding.ReplaceLots"/> turns the plan into the holding's lots.
/// </summary>
public sealed record LotPlan(DateOnly AcquiredOn, long Qty, decimal UnitCost, LotSource Source, bool UsesDefaultDate, DateOnly? ClosedOn = null, decimal? ClosePrice = null)
{
    public bool IsOpen => ClosedOn is null;
}
