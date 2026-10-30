using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

/// <summary>One stored break with its resolution, if any.</summary>
public sealed record StoredBreak(long Id, long RunId, Guid AccountId, BreakKind Kind, string Key, string? Symbol, long? IntentId, DateTimeOffset At, ResolutionRecord? Resolution);

/// <summary>The owner's resolution of a break (written by xd-api; read here).</summary>
public sealed record ResolutionRecord(DateTimeOffset At, string By, string Resolution);

/// <summary>
/// The insert-only record of runs and breaks (xd_exec writes it) and the read of the owner's resolutions (xd_app writes them). Nothing here edits a run or a break. A key with an unresolved row is not inserted again (the run only counts it), so one break is one row for the owner to resolve.
/// </summary>
public interface IReconStore
{
    /// <summary>Stores the run and the given new breaks; returns the run id.</summary>
    Task<long> RecordRunAsync(Guid accountId, ReconKind kind, bool readable, int orders, int trades, int positions, int breakCount, IReadOnlyList<ReconBreakDraft> newBreaks, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>The keys of breaks of this account and session day that have a resolution: a later run does not raise them again while the broker's figure is the same.</summary>
    Task<IReadOnlySet<string>> ResolvedKeysAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken);

    /// <summary>The keys of breaks of this account with no resolution yet.</summary>
    Task<IReadOnlySet<string>> OpenKeysAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredBreak>> BreaksAsync(Guid? accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconRunRecord>> RunsAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken);
}

/// <summary>Our side of the books for one account, read-only. There is no write method on purpose: reconciliation corrects nothing.</summary>
public sealed record OurAccountView(IReadOnlyList<OurLegView> Legs, IReadOnlyList<OurFillView> Fills, IReadOnlyList<OurPositionView> Positions, IReadOnlyList<OurIntentView> Intents);

public interface IReconOurSource
{
    Task<IReadOnlyList<Guid>> AccountsAsync(CancellationToken cancellationToken);

    Task<OurAccountView> LoadAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>The trades the portfolio stored for the session (P2-13, P11-01), for the journal-gap check. The execution role has no access to the portfolio schema: until a host is given one, nothing implements this and the check does not run.</summary>
public interface IStoredTrades
{
    Task<IReadOnlyList<Domain.Recon.StoredTrade>?> ForSessionAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken);
}
