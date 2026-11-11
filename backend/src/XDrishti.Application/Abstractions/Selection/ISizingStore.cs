using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Abstractions.Selection;

/// <summary>The sizing settings in force and the version they are at (the id of the newest settings row, 0 when none is stored).</summary>
/// <param name="Settings">The settings; the defaults (everything OFF) while no row exists.</param>
/// <param name="Version">The newest settings row id.</param>
public sealed record SizingState(SizingSettings Settings, long Version);

/// <summary>One stored row of the settings history.</summary>
public sealed record SizingChangeRow(long Id, string Key, string OldValue, string NewValue, string Direction, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>One stored row of the budget history; <paramref name="BudgetPct"/> null means the budget was removed (off).</summary>
public sealed record BudgetChangeRow(long Id, Guid BasketId, double? BudgetPct, string Direction, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>
/// The size scaling's owner settings and basket risk budgets behind a port (spec BK-10): <c>selection.sizing_settings_changes</c> and <c>selection.basket_risk_budgets</c>, both insert-only. The newest row per key (per basket) is current; with no row everything is OFF and no basket has a budget.
/// Only rows made by <see cref="SizingChanges"/> (owner actor) are appended. Nothing here places an order.
/// </summary>
public interface ISizingStore
{
    /// <summary>The settings in force and their version.</summary>
    ValueTask<SizingState> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The settings changes, newest first.</summary>
    ValueTask<IReadOnlyList<SizingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The budget in force per basket (a basket whose newest row removes the budget is left out), by basket id.</summary>
    ValueTask<IReadOnlyDictionary<Guid, double>> BudgetsAsync(CancellationToken cancellationToken);

    /// <summary>The budget changes, newest first.</summary>
    ValueTask<IReadOnlyList<BudgetChangeRow>> BudgetHistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Appends the rows if the newest settings row is still <paramref name="expectedVersion"/>; false when another change won.</summary>
    ValueTask<bool> AppendSettingsAsync(IReadOnlyList<SizingChange> rows, long expectedVersion, CancellationToken cancellationToken);

    /// <summary>Appends a budget row (<paramref name="budgetPct"/> null removes the budget) if the newest row of the basket is still the one the change was judged against; false when another change won.</summary>
    ValueTask<bool> AppendBudgetAsync(Guid basketId, double? expectedCurrentPct, double? budgetPct, SizingChange change, CancellationToken cancellationToken);
}
