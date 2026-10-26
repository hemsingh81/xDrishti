namespace XDrishti.Domain.Reporting;

/// <summary>
/// The writes the read-only rule of P8 allows (P8-00 criterion 5): <c>METHOD pattern</c> strings, compile-time. Empty in P8-17; P8-18 Part B adds the start and the cancel of a replay job (the owner approved the spec on 2026-10-26), P8-19 adds the five learning moves (the owner approved the spec on 2026-10-26), P8-20 adds the one export POST (the owner approved the spec on 2026-10-26). A route under a
/// guarded prefix that is not GET and not on this list makes the phase check say NOT MET.
/// </summary>
public static class ApprovedReportWrites
{
    /// <summary>The approved writes.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "POST /api/experiments/", "POST /api/experiments/jobs/{id:guid}/cancel", // P8-18 Part B (ADR 0129)
        "POST /api/lessons/{id:long}/link", "POST /api/lessons/{id:long}/close", "POST /api/lessons/{id:long}/dismiss", "POST /api/hypotheses/{id:long}/move", "POST /api/changelog/{id:long}/revert", // P8-19 (ADR 0130)
        "POST /api/exports/{kind}", // P8-20 (ADR 0131): a read that makes a file, a POST only because it is audited
    ];
}
