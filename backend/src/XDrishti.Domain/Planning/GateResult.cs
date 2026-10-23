namespace XDrishti.Domain.Planning;

/// <summary>The reading of one gate (spec P7-08): which gate, how it read and why in words (no secret, no account).</summary>
/// <param name="Gate">The gate id, at most <see cref="MaxGateLength"/> characters (the column of <c>plan_runs.failed_gate</c>).</param>
/// <param name="Status">Pass, fail or unavailable.</param>
/// <param name="Reason">Why, in words, at most <see cref="MaxReasonLength"/> characters (the column of <c>plan_runs.reason</c>).</param>
public sealed record GateResult(string Gate, GateStatus Status, string Reason)
{
    public const int MaxGateLength = 40;

    public const int MaxReasonLength = 500;

    /// <summary>Whether the run must stop here: anything but a pass.</summary>
    public bool Blocks => Status != GateStatus.Pass;

    /// <summary>The code stored and printed: pass, fail or unavailable.</summary>
    public string Code => Status switch
    {
        GateStatus.Pass => "pass",
        GateStatus.Fail => "fail",
        _ => "unavailable",
    };

    public static GateResult Passed(string gate, string reason) => new(gate, GateStatus.Pass, Clip(reason));

    public static GateResult Failed(string gate, string reason) => new(gate, GateStatus.Fail, Clip(reason));

    public static GateResult NotAvailable(string gate, string reason) => new(gate, GateStatus.Unavailable, Clip(reason));

    private static string Clip(string reason) => reason.Length <= MaxReasonLength ? reason : reason[..(MaxReasonLength - 1)] + "~";
}
