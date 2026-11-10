using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>
/// One SaveChanges is one transaction: the closed stays, the new members, the basket's new revision, the fetch row, the source state and the audit event (counts only) are written together or not at all. A concurrent apply that already
/// added the same instrument is stopped by the unique index on open stays: this one then writes nothing (false) and the next run sees the new membership.
/// </summary>
internal sealed class BasketMembershipApplier(XDrishtiDbContext db, BasketSourceStore store, IAccountEventLog events) : IBasketMembershipApplier
{
    public async ValueTask<bool> ApplyAsync(MembershipApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var basket = await db.Baskets.FirstAsync(b => b.Id == application.BasketId, cancellationToken);
        var open = await db.BasketMembers.Where(m => m.BasketId == application.BasketId && m.ValidTo == null).ToListAsync(cancellationToken);
        var actor = application.Fetch.Actor;

        var removed = application.Change.Removed.ToHashSet();
        foreach (var stay in open.Where(m => removed.Contains(m.InstrumentId)))
        {
            stay.Remove(actor, application.EffectiveFrom);
        }

        var present = open.Select(m => m.InstrumentId).ToHashSet();
        foreach (var id in application.Change.Added.Where(id => !present.Contains(id)))
        {
            var position = application.IncomingOrder.ToList().IndexOf(id);
            db.BasketMembers.Add(application.Origin is { } origin
                ? BasketMember.Listed(application.BasketId, id, position, application.EffectiveFrom, application.DetectedAt, origin.SourceRef, origin.SourceKind, origin.Provenance, origin.Notes.GetValueOrDefault(id), actor)
                : BasketMember.Fetched(application.BasketId, id, position, application.EffectiveFrom, application.DetectedAt, application.Fetch.Id, actor));
        }

        basket.Touch();
        store.Stage(application.Source, application.Fetch);
        events.Record(AccountEvent.Record(
            AccountEventType.BasketMembershipChanged, null, $"System basket '{application.BasketName}': {application.Change.Added.Count} added, {application.Change.Removed.Count} removed", actor, application.DetectedAt));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
