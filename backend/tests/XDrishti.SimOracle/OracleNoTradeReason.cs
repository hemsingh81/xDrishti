namespace XDrishti.SimOracle;

/// <summary>Why a candidate did not become a trade.</summary>
public enum OracleNoTradeReason
{
    NoFillBar,
    AfterSquareOff,
    EntryThroughStop,
    LimitNotReached,
    PositionOpen,
    OrderPendingAtEnd,
}
