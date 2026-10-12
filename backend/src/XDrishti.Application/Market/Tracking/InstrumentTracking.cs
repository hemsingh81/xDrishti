using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Tracking;

/// <summary>
/// The one place that tracks and untracks instruments, shared by the tracking endpoints and by features that track on the owner's behalf (baskets).
/// It only collects the changes (rows and one audit event) in the scoped database context: the caller saves them with its own unit of work, in the
/// same transaction as its own changes. Re-reading the state is the caller's job when a save is refused because of a concurrent change
/// (<see cref="IMarketUnitOfWork.TrySaveAsync"/>); the tracking rows carry a row version, so a save based on a row that someone else changed since it was read is refused.
/// </summary>
internal sealed class InstrumentTracking(ITrackedInstrumentStore store, IAccountEventLog events)
{
    /// <summary>
    /// Tracks the instruments, idempotently: never tracked ones get a row, untracked ones are tracked again (their history is kept), tracked ones are left alone.
    /// All or nothing: an unknown id fails with <c>instruments.invalid</c>, a retired instrument that is not tracked fails with <c>instruments.retired</c>
    /// (one that is tracked already stays as it is). <paramref name="via"/> names the feature for the audit text (for example "basket 'Core'").
    /// An audit event with the counts is collected only when something changed. A caller whose own change depends on the instruments staying tracked (a basket
    /// member) sets <paramref name="relyOnTracked"/>: the rows that are tracked already then take part in the save, which is refused when someone changed one of
    /// them (untracked it) since it was read. The plain tracking call does not, so tracking the same instruments twice at once does not collide.
    /// </summary>
    public async ValueTask<Result<TrackingOutcome>> TrackAsync(
        IReadOnlyCollection<Guid> instrumentIds, string actor, DateTimeOffset now, string? via, CancellationToken cancellationToken, bool relyOnTracked = false)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        var known = await ExistingAsync(ids, cancellationToken);
        if (known.IsFailure)
        {
            return Result.Failure<TrackingOutcome>(known.Error);
        }

        var tracked = (await store.FindAsync(ids, cancellationToken)).ToDictionary(t => t.Id);
        var refused = known.Value.Values.Where(i => i.Retired && !(tracked.TryGetValue(i.Id, out var t) && t.Active)).Select(i => i.Symbol).Order(StringComparer.Ordinal).ToList();
        if (refused.Count > 0)
        {
            return Result.Failure<TrackingOutcome>(MarketErrors.Retired(refused));
        }

        int added = 0, reactivated = 0, already = 0;
        foreach (var id in ids)
        {
            if (!tracked.TryGetValue(id, out var row))
            {
                store.Add(TrackedInstrument.Track(id, actor, now));
                added++;
            }
            else if (row.Reactivate(actor, now))
            {
                reactivated++;
            }
            else
            {
                if (relyOnTracked)
                {
                    store.RelyOn(row);
                }

                already++;
            }
        }

        var outcome = new TrackingOutcome(ids.Length, added, reactivated, already);
        if (outcome.Changed > 0)
        {
            events.Record(AccountEvent.Record(
                AccountEventType.InstrumentsTracked,
                null,
                $"Tracked {outcome.Changed} of {outcome.Requested} instruments ({added} new, {reactivated} tracked again, {already} already tracked){Via(via)}",
                actor,
                now));
        }

        return Result.Success(outcome);
    }

    /// <summary>
    /// Untracks the instruments, idempotently: tracked ones become inactive (history kept), the others are left alone. All or nothing: an unknown id
    /// fails with <c>instruments.invalid</c>. An audit event with the counts is collected only when something changed.
    /// </summary>
    public async ValueTask<Result<UntrackOutcome>> UntrackAsync(
        IReadOnlyCollection<Guid> instrumentIds, string actor, DateTimeOffset now, string? via, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        var known = await ExistingAsync(ids, cancellationToken);
        if (known.IsFailure)
        {
            return Result.Failure<UntrackOutcome>(known.Error);
        }

        var untracked = 0;
        foreach (var row in await store.FindAsync(ids, cancellationToken))
        {
            if (row.Deactivate(now))
            {
                untracked++;
            }
        }

        var outcome = new UntrackOutcome(ids.Length, untracked, ids.Length - untracked);
        if (untracked > 0)
        {
            events.Record(AccountEvent.Record(
                AccountEventType.InstrumentsUntracked,
                null,
                $"Untracked {untracked} of {outcome.Requested} instruments ({outcome.NotTracked} were not tracked){Via(via)}",
                actor,
                now));
        }

        return Result.Success(outcome);
    }

    private async ValueTask<Result<Dictionary<Guid, TrackableInstrument>>> ExistingAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        var found = (await store.FindInstrumentsAsync(ids, cancellationToken)).ToDictionary(i => i.Id);
        var missing = ids.Count(id => !found.ContainsKey(id));
        return missing == 0
            ? Result.Success(found)
            : Result.Failure<Dictionary<Guid, TrackableInstrument>>(MarketErrors.Invalid("instrumentIds", missing == 1 ? "1 instrument does not exist." : $"{missing} instruments do not exist."));
    }

    private static string Via(string? via) => string.IsNullOrWhiteSpace(via) ? string.Empty : $" via {via.Trim()}";
}
