namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// The largest peak-to-trough fall of a NAV series: <c>Pct</c> is a percent number (12.5 means it fell 12.5 %) from the peak on <c>From</c> to the low on <c>To</c>. A series that never falls (flat or rising)
/// gives 0 and no dates.
/// </summary>
public sealed record Drawdown(decimal Pct, DateOnly? From, DateOnly? To);
