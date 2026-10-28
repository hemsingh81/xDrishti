using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>One stored change of the live switches.</summary>
public sealed record StoredLiveSettings(long Id, string Hash, bool AutoApproveTickets, bool AutoRampUp, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The live switches (<c>trading.live_settings</c>, spec P11-04, ADR 0148): insert-only, the newest row current, no row = both OFF. The hash and the time are stamped by the database.</summary>
public interface ILiveSettingsStore
{
    /// <summary>The settings in force, or the defaults (both OFF, no id).</summary>
    ValueTask<LiveSettings> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<StoredLiveSettings>> HistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Appends one row if the newest row is still <paramref name="expectedCurrentId"/> (null for none); null when another change won.</summary>
    ValueTask<StoredLiveSettings?> AppendAsync(string actor, bool autoApproveTickets, bool autoRampUp, string reason, long? expectedCurrentId, CancellationToken cancellationToken);
}
