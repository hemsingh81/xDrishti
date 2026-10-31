namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>One change of the owner's tick-recorder switch (<c>market.tick_recorder_switch</c>, insert-only).</summary>
public sealed record TickSwitchRow(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The owner's stored start switch for the trade-tick recorder (P7-16b). No row means OFF.</summary>
public interface ITickSwitchStore
{
    /// <summary>The newest row; null when nothing was ever stored (OFF).</summary>
    ValueTask<TickSwitchRow?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<TickSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken);

    /// <summary>Appends a row (the database stamps the time) and returns it.</summary>
    ValueTask<TickSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}
