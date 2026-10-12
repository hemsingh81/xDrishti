using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Abstractions.Market.Actions;

/// <summary>An instrument as the corporate actions need it.</summary>
public sealed record ActionInstrument(Guid Id, string Symbol, string Name);

/// <summary>The suggestions waiting for the owner: how many and when the newest was found (null when none).</summary>
public sealed record PendingActions(int Count, DateTimeOffset? LatestAt);

/// <summary>An action's identity: one per instrument, ex-date and kind.</summary>
public sealed record ActionKey(Guid InstrumentId, DateOnly ExDate, CorporateActionKind Kind);

/// <summary>Reads of the corporate actions (all reads, no tracking).</summary>
public interface ICorporateActionQueries
{
    ValueTask<CorporateActionPage> ListAsync(CorporateActionSearch search, CancellationToken cancellationToken);

    ValueTask<PendingActions> GetPendingAsync(CancellationToken cancellationToken);

    /// <summary>The applied actions of the instrument, by ex-date.</summary>
    ValueTask<IReadOnlyList<AppliedAction>> ListAppliedAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>
    /// The ex-dates the detection must not suggest again for the instrument: the ex-dates it has an action on, whatever its status (also ignored ones), and the dates an action was first detected
    /// on, even when the owner has moved it to another ex-date since.
    /// </summary>
    ValueTask<IReadOnlySet<DateOnly>> ListExDatesAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>The keys of the actions that exist for these instruments, whatever their status.</summary>
    ValueTask<IReadOnlySet<ActionKey>> ListKeysAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    /// <summary>
    /// For each of these instruments the date its loaded CSV files reach: the latest bar of the files not undone. The owner's files are already adjusted for corporate actions at source, so an action on or
    /// before this date would adjust their prices twice. Instruments without a loaded file (or whose files predate the recorded range) are missing from the answer.
    /// </summary>
    ValueTask<IReadOnlyDictionary<Guid, DateOnly>> ListCsvAdjustedThroughAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    ValueTask<ActionInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken);
}
