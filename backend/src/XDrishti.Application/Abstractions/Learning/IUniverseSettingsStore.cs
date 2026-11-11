using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>One accepted change of a universe setting, as appended to the history.</summary>
/// <param name="Key">The setting key.</param>
/// <param name="OldValue">The value in force before.</param>
/// <param name="NewValue">The new value as text.</param>
/// <param name="Actor">The owner actor.</param>
/// <param name="Hash">The hash of the change.</param>
public sealed record UniverseSettingChange(string Key, string OldValue, string NewValue, string Actor, string Hash);

/// <summary>A stored change with its id and time.</summary>
public sealed record StoredUniverseSettingChange(long Id, string Key, string OldValue, string NewValue, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>The settings in force and the latest changes, newest first.</summary>
/// <param name="Settings">The settings in force (the defaults for a key with no row).</param>
/// <param name="History">The latest 50 changes.</param>
public sealed record UniverseSettingsState(UniverseSettings Settings, IReadOnlyList<StoredUniverseSettingChange> History);

/// <summary><c>research.universe_settings_changes</c> (BK-11): insert-only history, the newest row per key is current.</summary>
public interface IUniverseSettingsStore
{
    /// <summary>The settings in force and the history.</summary>
    ValueTask<UniverseSettingsState> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Appends the changes in one transaction.</summary>
    ValueTask AppendAsync(IReadOnlyList<UniverseSettingChange> changes, CancellationToken cancellationToken);
}
