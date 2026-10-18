namespace XDrishti.Domain.Research;

/// <summary>
/// Why a simulated trade ended (ADR 0037, decision D4). Stored by name, never renamed. <see cref="EndOfData"/> is data, not a rule: the day's bars ran out while the position was open, so the trade is reported apart and
/// never counted among the trades that statistics use.
/// </summary>
public enum ExitReason
{
    /// <summary>The stop level was touched (or the market gapped through it and the exit is at that minute's open).</summary>
    Stop,

    /// <summary>The target level was touched.</summary>
    Target,

    /// <summary>The square-off minute began: the position left at its open.</summary>
    SquareOff,

    /// <summary>The bars of the entry date ended first: the position left at the close of the last minute.</summary>
    EndOfData,
}
