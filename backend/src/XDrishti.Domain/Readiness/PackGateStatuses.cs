namespace XDrishti.Domain.Readiness;

/// <summary>
/// The words of the <c>verify-pack --gate</c> decision as the owner records them (spec CF-11 row 15, ADR 0226) and the one rule for what counts: only <c>PASS</c> over at least 20 trades is a pass. Every other status is a fail. "No record" is not a status here: it is the
/// absence of a value, which the reader reports as unavailable (also a fail).
/// </summary>
public static class PackGateStatuses
{
    /// <summary>The spelling of the passing status.</summary>
    public const string Pass = "PASS";

    /// <summary>The trades a pack must hold for the P3-09 check.</summary>
    public const int MinimumTrades = 20;

    private static readonly string[] Known = [Pass, "PASS_WITH_DATA_FINDINGS", "RERUN_NEEDED", "DATA_REPAIR_NEEDED", "BLOCKED_DEFECT"];

    /// <summary>Every status the gate can print, in its own spelling.</summary>
    public static IReadOnlyList<string> All => Known;

    /// <summary>Whether the text is one of the gate's statuses (exact spelling).</summary>
    public static bool IsKnown(string? status) => status is not null && Array.IndexOf(Known, status) >= 0;

    /// <summary>Whether a record counts as a pass: the status is exactly <c>PASS</c> and the pack held at least 20 trades.</summary>
    public static bool Counts(string? status, int tradeCount) => string.Equals(status, Pass, StringComparison.Ordinal) && tradeCount >= MinimumTrades;
}
