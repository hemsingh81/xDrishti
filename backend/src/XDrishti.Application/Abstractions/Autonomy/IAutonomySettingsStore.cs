using XDrishti.Domain.Autonomy;

namespace XDrishti.Application.Abstractions.Autonomy;

/// <summary>
/// The autonomy settings behind a port (spec P7-12, ADR 0091): <c>workflow.autonomy_settings</c>, insert-only. A change is a new row; the newest row is current; with no row everything is OFF. Only <see cref="AutonomyChange"/> (owner actor only)
/// can be appended. Nothing here places an order.
/// </summary>
public interface IAutonomySettingsStore
{
    /// <summary>The settings in force: the newest row, or <see cref="AutonomySettings.Defaults"/> (all OFF) when none.</summary>
    ValueTask<AutonomySettings> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<AutonomyChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Appends the change if the newest row is still the one the caller saw (<paramref name="expectedCurrentId"/>, null for none); returns the new row, or null when another change won.</summary>
    ValueTask<AutonomyChangeRow?> AppendAsync(AutonomyChange change, long? expectedCurrentId, CancellationToken cancellationToken);
}

/// <summary>One stored change of the settings.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Hash">The snapshot hash the database stamped.</param>
/// <param name="AutoApproveTickets"><c>autonomy.auto_approve_tickets</c> after the change.</param>
/// <param name="PromoteFromReserve"><c>autonomy.promote_from_reserve</c> after the change.</param>
/// <param name="MinPWin">The threshold after the change.</param>
/// <param name="MaxAutoPerDay">The cap after the change.</param>
/// <param name="Actor">Who (an owner actor).</param>
/// <param name="Reason">Why.</param>
/// <param name="ChangedAt">When (database clock).</param>
public sealed record AutonomyChangeRow(long Id, string Hash, bool AutoApproveTickets, bool PromoteFromReserve, double MinPWin, int MaxAutoPerDay, string Actor, string Reason, DateTimeOffset ChangedAt);
