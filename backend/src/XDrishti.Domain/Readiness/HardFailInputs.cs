namespace XDrishti.Domain.Readiness;

/// <summary>
/// The terms of the hard fails of the readiness score (spec CF-11 D3) other than "an input of an applicable sub-score is unavailable", which the score derives itself. Each term reads clear, fault, unavailable (a hard fail) or not applicable.
/// </summary>
public sealed record HardFailInputs
{
    /// <summary>An integrity fault (the P7 and P10 integrity checks; the exact list is unverified, so there is no source yet).</summary>
    public FaultState Integrity { get; init; }

    /// <summary>The cost sheet is not confirmed, or not checked against a bill (P10-00 D5, CF-05).</summary>
    public FaultState CostSheet { get; init; }

    /// <summary>The CF-09 failure boundary was crossed in this paper epoch.</summary>
    public FaultState FailureBoundary { get; init; }

    /// <summary>The hold-out was reused for a pass claim (CF-06).</summary>
    public FaultState HoldOutReused { get; init; }

    /// <summary>The evidence comes only from tier C instruments.</summary>
    public FaultState TierCOnly { get; init; }

    /// <summary>A CF-12 kill-rule demotion is active.</summary>
    public FaultState KillDemotion { get; init; }

    /// <summary>A fully clear set (for tests and for a strategy with nothing to fear).</summary>
    public static HardFailInputs AllClear { get; } = new()
    {
        Integrity = FaultState.Clear,
        CostSheet = FaultState.Clear,
        FailureBoundary = FaultState.Clear,
        HoldOutReused = FaultState.Clear,
        TierCOnly = FaultState.Clear,
        KillDemotion = FaultState.Clear,
    };
}
