namespace XDrishti.Application.Assistant;

/// <summary>A lot as the assistant's portfolio view shows it.</summary>
public sealed record PortfolioLotRow(DateOnly AcquiredOn, long Qty, decimal UnitCost, DateOnly? ClosedOn, decimal? ClosePrice, bool Estimated);
