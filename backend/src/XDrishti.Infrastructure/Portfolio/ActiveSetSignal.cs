using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// Decides, from what a unit of work is about to save, whether the live feed's active set may have changed (ADR 0024 §6, P2-10 criterion 8): a holding added (a sync, an import, a trade rebuild that brings one back), or one whose open/closed
/// state or instrument changed, or a price alert created (tracked), or the audit event of a price alert created, triggered or cancelled (a trigger and a cancel are conditional updates that bypass the tracker, but each is saved with its event
/// in the same unit of work). The one place that knows which changes matter, so no use case has to send the signal itself; a false positive costs the feed one extra recompute.
/// </summary>
internal static class ActiveSetSignal
{
    public static bool IsAffectedBy(ChangeTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (Affects(entry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Affects(EntityEntry entry) => entry.Entity switch
    {
        Holding => entry.State != EntityState.Modified || entry.Property(nameof(Holding.Status)).IsModified || entry.Property(nameof(Holding.InstrumentId)).IsModified,
        PriceAlert => entry.State != EntityState.Modified || entry.Property(nameof(PriceAlert.Status)).IsModified,
        AccountEvent changed => entry.State == EntityState.Added && changed.Event is AccountEventType.PriceAlertCreated or AccountEventType.PriceAlertTriggered or AccountEventType.PriceAlertCancelled,
        _ => false,
    };
}
