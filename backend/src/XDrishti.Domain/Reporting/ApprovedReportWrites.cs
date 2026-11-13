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
        "POST /api/learning/rollback/{id:long}/apply", "PUT /api/learning/ml-settings", "POST /api/learning/models/{id:long}/promote", "POST /api/learning/models/{id:long}/holdout-score", // CF-10 part 10d-1 (ADR 0220): the owner's apply of a rollback proposal, the owner's three ML switches and the owner's promotion of a model, and (CF-10e part 3, ADR 0285) the owner's one hold-out score of a model, behind a fresh password; the routes of the CF-10 spec, which the owner approved on 2026-11-03
        "POST /api/cells/universe-links/{key}/propose", // BK-11b (ADR 0266): the owner act that proposes one scope hypothesis from a stable tier observation; fresh password, applies nothing
        "PUT /api/reports/basket-benchmark", // BK-06c (ADR 0262): the owner's choice of the basket profile's index; a session, no password, it loosens nothing
        "POST /api/learning/cells/{key}/re-enable", // CF-12 part 12b (ADR 0219): the owner's re-enable of a paused cell, with a fresh password; the route of the CF-12 spec, which the owner approved on 2026-11-03
    ];
}
