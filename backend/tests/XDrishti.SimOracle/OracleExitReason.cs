namespace XDrishti.SimOracle;

/// <summary>Why a trade left.</summary>
public enum OracleExitReason
{
    Stop,
    Target,
    SquareOff,
    EndOfData,
    MaxHold,
    EmaExit,
}
