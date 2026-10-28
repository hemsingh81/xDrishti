namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The slippage of one entry (spec P11-02): positive is always worse for the owner. <see cref="Total"/> is rupees per share; <see cref="Delay"/> and <see cref="Execution"/> are null (never zero) when there is no arrival price, and when present they add up to
/// <see cref="Total"/> exactly. Ticks and R are null when the tick or the risk per share is not known.
/// </summary>
public sealed record SlippageParts(decimal Total, decimal? Delay, decimal? Execution, double? TotalTicks, double TotalBps, double? TotalR, double? DelayR, double? ExecutionR);
