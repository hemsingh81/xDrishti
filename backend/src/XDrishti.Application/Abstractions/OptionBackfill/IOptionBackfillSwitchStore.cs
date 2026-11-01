namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>One change of the owner's backfill switch (<c>options.backfill_switch</c>, insert-only).</summary>
public sealed record BackfillSwitchRow(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The owner's stored start switch for the expired-options backfill (P14-03). No row means OFF.</summary>
public interface IOptionBackfillSwitchStore
{
    /// <summary>The newest row; null when nothing was ever stored (OFF).</summary>
    ValueTask<BackfillSwitchRow?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<BackfillSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken);

    /// <summary>Appends a row (the database stamps the time) and returns it.</summary>
    ValueTask<BackfillSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}

/// <summary>The owner's note that the probe result was read and accepted (<c>options.backfill_probe_ack</c>, insert-only).</summary>
public sealed record BackfillProbeAck(long Id, string Actor, string Note, DateTimeOffset AcceptedAt);

/// <summary>The stored acceptance of the probe result (P14-03 criterion 9): the backfill refuses to run until a row exists. Written by an owner act with a fresh password.</summary>
public interface IBackfillProbeAckStore
{
    /// <summary>The newest acknowledgement; null when the probe result was never accepted.</summary>
    ValueTask<BackfillProbeAck?> CurrentAsync(CancellationToken cancellationToken);

    ValueTask<BackfillProbeAck> AppendAsync(string actor, string note, CancellationToken cancellationToken);
}
