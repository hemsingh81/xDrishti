namespace XDrishti.Simulation.Walking;

/// <summary>What an exit rule may ask of the walker (P4-01, D5). An add of shares is P4-04's.</summary>
internal enum ExitActionKind
{
    /// <summary>Move the stop to a level; a level looser than the stop is ignored and counted (D6).</summary>
    MoveStop,

    /// <summary>Move the target to a level.</summary>
    MoveTarget,

    /// <summary>Take a share of the maximum quantity out of the position at the open of the minute that sees the request.</summary>
    ExitShare,

    /// <summary>Take the whole position out at the open of the first minute that holds a trade at or after the one that sees the request, unless that open is already through the stop (P4-03, D7); the walker reports it as <c>EmaExit</c>.</summary>
    SignalExit,
}
