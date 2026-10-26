namespace XDrishti.Application.Abstractions.Depth;

/// <summary>One change of the owner's depth-recorder switch (<c>market.depth_recorder_switch</c>, insert-only).</summary>
public sealed record DepthSwitchRow(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The owner's stored start switch for the depth recorder (P7-16). No row means OFF.</summary>
public interface IDepthSwitchStore
{
    /// <summary>The newest row; null when nothing was ever stored (OFF).</summary>
    ValueTask<DepthSwitchRow?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<DepthSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken);

    /// <summary>Appends a row (the database stamps the time) and returns it.</summary>
    ValueTask<DepthSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}
