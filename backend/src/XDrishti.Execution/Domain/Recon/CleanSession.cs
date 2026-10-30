namespace XDrishti.Execution.Domain.Recon;

/// <summary>One stored reconciliation run.</summary>
public sealed record ReconRunRecord(long Id, DateTimeOffset At, Guid AccountId, ReconKind Kind, bool Readable, int Breaks);

/// <summary>
/// A session counts as clean for P12-12 only if the end-of-day and the final run both ran, both were readable and had no breaks, and the session had no break row at all, resolved ones included (spec P12-06 criterion 10). A run that could not read the broker
/// is unavailable, never clean.
/// </summary>
public static class CleanSession
{
    public static bool IsClean(IReadOnlyList<ReconRunRecord> runsOfTheSession, int breakRowsOfTheSession)
    {
        ArgumentNullException.ThrowIfNull(runsOfTheSession);
        return breakRowsOfTheSession == 0
            && runsOfTheSession.All(r => r.Readable && r.Breaks == 0)
            && runsOfTheSession.Any(r => r.Kind == ReconKind.Eod)
            && runsOfTheSession.Any(r => r.Kind == ReconKind.Final);
    }
}
