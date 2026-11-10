using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>Builds the views of the stored sizing settings and budgets.</summary>
internal static class SizingViews
{
    public const int HistoryLimit = 50;

    public const string Notice = "Sizing only scales a suggestion down, never up. Advisory: no order is placed in any setting. Every change needs the owner's password.";

    public static async ValueTask<SizingSettingsView> SettingsAsync(ISizingStore store, CancellationToken cancellationToken)
    {
        var state = await store.CurrentAsync(cancellationToken);
        var history = (await store.HistoryAsync(HistoryLimit, cancellationToken)).Select(r => new SizingChangeView(r.Id, r.Key, r.OldValue, r.NewValue, r.Direction, r.Actor, r.ChangedAt, r.Hash)).ToList();
        var s = state.Settings;
        return new SizingSettingsView(s.VolTargetEnabled, s.Floor, s.CapacityEnabled, s.CapacityPct, state.Version, Notice, history.FirstOrDefault(h => h.Direction == SizingChange.Loosen), history);
    }

    public static async ValueTask<BasketRiskBudgetsView> BudgetsAsync(ISizingStore store, CancellationToken cancellationToken)
    {
        var budgets = (await store.BudgetsAsync(cancellationToken)).OrderBy(p => p.Key).Select(p => new BasketBudgetView(p.Key, p.Value)).ToList();
        var history = (await store.BudgetHistoryAsync(HistoryLimit, cancellationToken)).Select(r => new BudgetChangeView(r.Id, r.BasketId, r.BudgetPct, r.Direction, r.Actor, r.ChangedAt, r.Hash)).ToList();
        return new BasketRiskBudgetsView(budgets, Notice, history);
    }
}
