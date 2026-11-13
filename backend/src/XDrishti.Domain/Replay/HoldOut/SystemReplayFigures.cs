namespace XDrishti.Domain.Replay.HoldOut;

/// <summary>
/// The figures the verdict reads (SR-03 D3). A null is <c>unavailable</c> and can never make a pass.
/// </summary>
/// <param name="Trades">Closed trades of the selection arm.</param>
/// <param name="NetBasis">Whether every closed trade was charged by the checked sheet (the figures are net).</param>
/// <param name="CagrPct">Compound annual growth, percent.</param>
/// <param name="MaxDrawdownPct">Maximum drawdown, percent.</param>
/// <param name="ArmAnswer">The SR-02 wire word of the selection-versus-baseline comparison.</param>
public sealed record SystemReplayFigures(int? Trades, bool NetBasis, double? CagrPct, decimal? MaxDrawdownPct, string? ArmAnswer);
