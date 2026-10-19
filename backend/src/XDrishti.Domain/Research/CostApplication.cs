namespace XDrishti.Domain.Research;

/// <summary>
/// The outcome of costing one trade (ADR 0042, D13): either the trade, kept and carrying its costing, or the reason it was skipped. Exactly one of <see cref="Trade"/> and <see cref="Skip"/> is set; only <see cref="Kept"/> and
/// <see cref="Skipped"/> build one.
/// </summary>
public sealed record CostApplication
{
    private CostApplication(SimTrade? trade, TradeSkip? skip)
    {
        Trade = trade;
        Skip = skip;
    }

    /// <summary>The costed trade; null when the trade was skipped.</summary>
    public SimTrade? Trade { get; }

    /// <summary>Why the trade was skipped; null when it was kept.</summary>
    public TradeSkip? Skip { get; }

    /// <summary>The trade was kept.</summary>
    /// <exception cref="ArgumentNullException">The trade is null.</exception>
    public static CostApplication Kept(SimTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return new CostApplication(trade, null);
    }

    /// <summary>The trade was skipped for this reason.</summary>
    public static CostApplication Skipped(TradeSkip skip) => new(null, skip);
}
