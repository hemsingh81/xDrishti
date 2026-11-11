using XDrishti.Domain.Selection;

namespace XDrishti.Application.Abstractions.Selection;

/// <summary>The basket settings in force and the version they are at (the id of the newest row, 0 when none is stored).</summary>
/// <param name="Settings">The settings; the defaults (tracked set, no cap, 0.70) while no row exists.</param>
/// <param name="Version">The newest row id.</param>
public sealed record BasketSettingsState(BasketSettings Settings, long Version);

/// <summary>One stored row of the basket settings history.</summary>
public sealed record BasketSettingChangeRow(long Id, string Key, string OldValue, string NewValue, string Direction, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>
/// The basket settings behind a port (spec BK-08): <c>selection.basket_settings_changes</c>, insert-only. The newest row per key is current; with no row everything is OFF (breadth of the tracked set, no cluster cap). Only rows made by
/// <see cref="BasketSettingChanges"/> (owner actor) are appended. Nothing here places an order.
/// </summary>
public interface IBasketSettingsStore
{
    /// <summary>The settings in force and their version.</summary>
    ValueTask<BasketSettingsState> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<BasketSettingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Appends the rows if the newest row is still <paramref name="expectedVersion"/>; false when another change won.</summary>
    ValueTask<bool> AppendAsync(IReadOnlyList<BasketSettingChange> rows, long expectedVersion, CancellationToken cancellationToken);
}
