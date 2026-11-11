using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>One stored change row looked up by its notice id: the notice and the direction it was stored with.</summary>
/// <param name="Notice">The change as a notice.</param>
/// <param name="Direction">tighten, loosen or neutral, as stored (never recomputed).</param>
public sealed record LooseningLookup(LooseningNotice Notice, string Direction);

/// <summary>A dismissal to append. An automatic one carries the setting row in force and its hash; an owner one carries neither.</summary>
public sealed record NewLooseningDismissal(string Source, long Id, string Actor, long? SettingId, string? SettingHash);

/// <summary>The automatic dismissal setting in force: no row means OFF.</summary>
/// <param name="Id">The newest row id; null when none.</param>
/// <param name="AutoDismiss">The value.</param>
/// <param name="Hash">The hash of the newest row.</param>
public sealed record LooseningSwitch(long? Id, bool AutoDismiss, string? Hash);

/// <summary>One stored row of the setting history.</summary>
public sealed record LooseningSettingRow(long Id, bool AutoDismiss, string ChangedBy, DateTimeOffset ChangedAt, string Hash);

/// <summary>
/// The loosening notices behind a port (BK-10b): a read over the three insert-only change tables (rows with direction loosen) and <c>trading.loosening_dismissals</c>, the dismissals (insert-only, one per change) and the automatic setting (<c>trading.loosening_settings</c>, insert-only).
/// Nothing here writes a limit, a stored plan or its hash.
/// </summary>
public interface ILooseningStore
{
    /// <summary>Every loosening with no dismissal, oldest first, in one query.</summary>
    ValueTask<IReadOnlyList<LooseningNotice>> ListUndismissedAsync(CancellationToken cancellationToken);

    /// <summary>One change row by its notice parts, or null when none exists.</summary>
    ValueTask<LooseningLookup?> FindAsync(string source, long id, CancellationToken cancellationToken);

    /// <summary>Appends a dismissal; false when the change was dismissed already (nothing is added). The database refuses an automatic row whose setting is not the newest or is OFF.</summary>
    ValueTask<bool> AddDismissalAsync(NewLooseningDismissal dismissal, CancellationToken cancellationToken);

    /// <summary>When the newest plan stored before the given one was built; null when there is none.</summary>
    ValueTask<DateTimeOffset?> PreviousPlanCreatedAtAsync(long planId, CancellationToken cancellationToken);

    /// <summary>The automatic dismissal setting in force.</summary>
    ValueTask<LooseningSwitch> CurrentSettingAsync(CancellationToken cancellationToken);

    /// <summary>The setting rows, newest first.</summary>
    ValueTask<IReadOnlyList<LooseningSettingRow>> SettingHistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Appends a setting row if the newest row is still <paramref name="expectedVersion"/> (0 when none); false when another change won.</summary>
    ValueTask<bool> AppendSettingAsync(bool autoDismiss, string actor, string hash, long expectedVersion, CancellationToken cancellationToken);
}
