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

    /// <summary>The bars ended first: the position left at the close of the last minute that holds a trade (an intraday trade: the bars of the entry date; a swing trade: the list of bars).</summary>
    EndOfData,

    /// <summary>A swing position reached the close of the last session it may be held (ADR 0040, X8): the close of the due session's last 1-minute bar, or of the last minute that holds a trade when the due minute is missing.</summary>
    MaxHold,

    /// <summary>The <c>time_stop</c> overlay found too little progress (P4-02, D8 and D9): the open share left at the open of the minute after the check bar (intraday) or at the close of the checked session (swing).</summary>
    TimeStop,
}
