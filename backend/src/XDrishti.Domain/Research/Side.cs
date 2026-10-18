namespace XDrishti.Domain.Research;

/// <summary>
/// The side of a research trade: a strategy buys (<see cref="Buy"/>, goes long) or sells (<see cref="Sell"/>, goes short). Used by the strategy model (P3-02b, ADR 0036) and by the simulator that follows (P3-03).
/// It is <b>not</b> <c>XDrishti.Domain.Portfolio.TradeSide</c>: that enum is a stored ledger fact with its own rule ("stored by name, never rename a value"), while a research side has its own schema versions (the strategy
/// model, the simulated trade); a trade that meets a ledger row is mapped by name.
/// </summary>
public enum Side
{
    /// <summary>The strategy buys first (a long trade).</summary>
    Buy,

    /// <summary>The strategy sells first (a short trade).</summary>
    Sell,
}
