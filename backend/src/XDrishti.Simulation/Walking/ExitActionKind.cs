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
}
