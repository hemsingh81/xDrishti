namespace XDrishti.Simulation.Walking;

/// <summary>Which exit set with a partial the walk runs (P4-02): none (<c>FIXED_RR</c>), <c>PARTIAL_BE</c> or <c>PARTIAL_TRAIL</c>.</summary>
internal enum PartialKind
{
    /// <summary>No partial exit: the position has its stop and target only (with or without the time stop).</summary>
    None,

    /// <summary>Book part at T1, stop to the entry, the rest at T2 (D1 to D3).</summary>
    Be,

    /// <summary>Book part at T1, trail the rest by the ATR (D4 to D6).</summary>
    Trail,
}

/// <summary>
/// The parameters of the policies that P4-02 adds to the walk, as decimals (the walker computes in decimal): the partial (<paramref name="Book"/> is the share booked at T1, <paramref name="T2Rr"/> the second target of
/// <c>PARTIAL_BE</c>, <paramref name="TrailMult"/> the multiple of the ATR of <c>PARTIAL_TRAIL</c>) and the <c>time_stop</c> overlay (<paramref name="MinProgressRr"/> is null when there is none; <paramref name="IntradayBars"/> or
/// <paramref name="SwingSessions"/>, the one of the style, is above 0). T1 itself is the target of the position (<c>t1_rr</c> as the reward-to-risk of the walk).
/// </summary>
internal sealed record LegPolicy(PartialKind Partial, decimal Book, decimal T2Rr, decimal TrailMult, decimal? MinProgressRr, int IntradayBars, int SwingSessions);
