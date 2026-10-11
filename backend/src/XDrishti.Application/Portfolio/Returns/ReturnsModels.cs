namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// Profit and loss of a holding, a lot or a group. <see cref="Invested"/> is what the open lots cost, <see cref="Value"/> what they are worth now,
/// <see cref="RealisedPnl"/> what closed lots earned (<see cref="RealisedEstimated"/> when any sale price is the last seen price, not the real one).
/// <see cref="PnlPct"/> and <see cref="Xirr"/> are null when there is nothing invested / no meaningful rate. For a holding without a usable price
/// (<see cref="XirrState.NoPrice"/>) <see cref="Value"/>, <see cref="Pnl"/> and <see cref="PnlPct"/> are null: its cost is known, its worth is not.
/// </summary>
public sealed record ReturnFigures(
    decimal Invested,
    decimal? Value,
    decimal? Pnl,
    decimal? PnlPct,
    XirrResult Xirr,
    decimal RealisedPnl,
    bool RealisedEstimated);
