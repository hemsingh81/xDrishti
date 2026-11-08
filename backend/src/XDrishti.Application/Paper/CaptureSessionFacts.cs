using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// The capture fields of one session's fact for the paper phase check, from the session's capture rows. The one place that reads them, shared by the whole phase check and by the narrow read of criterion I (CF-11, ADR 0224), so the two cannot read a session differently.
/// The fields that are not about the capture (plan, nightly attempts) are left at their empty values for the caller to set.
/// </summary>
internal static class CaptureSessionFacts
{
    public static PhaseSessionFact Of(DateOnly date, IEnumerable<CaptureRunView> rowsOfSession)
    {
        ArgumentNullException.ThrowIfNull(rowsOfSession);
        var rows = rowsOfSession.OrderBy(r => r.Id).ToList();
        var newest = rows.Count == 0 ? null : rows[^1];
        return new PhaseSessionFact(date, false, 0, 0, null, rows.Count, newest is { Outcome: "done", Pending: 0 }, newest is { Pending: > 0 }, rows.Sum(r => r.Integrity));
    }
}
