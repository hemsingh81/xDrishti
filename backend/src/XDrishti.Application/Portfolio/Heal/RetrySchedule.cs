using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Portfolio.Heal;

/// <summary>
/// When a failed sync is tried again quietly (P2-16, ADR 0027). Only a transient failure is, at most <see cref="IHealSettings.RetryBackoff"/> times in the day it happened on: the first retry after the first wait (default 1 minute), the second
/// after the second (5 minutes), the third after the third (15 minutes); a retry that would fall on the next India day is not made (the next scheduled run is the retry). Pure: the failed attempts of the day are <see cref="SyncHealth.Attempts"/>.
/// </summary>
internal static class RetrySchedule
{
    /// <summary>When the next retry is due; null when none follows (healthy, a permanent reason, all retries made, or the day would be over).</summary>
    public static DateTimeOffset? NextAttemptAt(SyncHealth health, IHealSettings settings)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(settings);
        if (health.LastFailedAt is not { } failedAt || !SyncFailures.IsTransient(health.LastFailureReason))
        {
            return null;
        }

        // Attempts failed so far today: 1 = the first attempt failed, so the retry after the first wait is next; with as many failures as there are waits plus one, every retry was made.
        if (health.Attempts < 1 || health.Attempts > settings.RetryBackoff.Count)
        {
            return null;
        }

        var due = failedAt + settings.RetryBackoff[health.Attempts - 1];
        return IndiaTime.Today(due) == IndiaTime.Today(failedAt) ? due : null;
    }

    /// <summary>True when a retry is due now: its wait is over and it is still the day of the failure (a failure of yesterday is left to the next scheduled run).</summary>
    public static bool IsDue(SyncHealth health, IHealSettings settings, DateTimeOffset now) =>
        NextAttemptAt(health, settings) is { } due && due <= now && health.LastFailedAt is { } failedAt && IndiaTime.Today(now) == IndiaTime.Today(failedAt);

    /// <summary>True while retries remain: the owner is told nothing about this failure yet. After the day of the failure ends, the next scheduled run is the only retry left.</summary>
    public static bool RetriesRemain(SyncHealth health, IHealSettings settings, DateTimeOffset now) =>
        NextAttemptAt(health, settings) is not null && health.LastFailedAt is { } failedAt && IndiaTime.Today(now) == IndiaTime.Today(failedAt);
}
