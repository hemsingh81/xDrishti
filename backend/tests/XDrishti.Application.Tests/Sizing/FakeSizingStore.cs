using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Tests.Sizing;

/// <summary>An in-memory <see cref="ISizingStore"/> with the real rules: insert-only, newest row wins, a stale version or a stale budget is refused.</summary>
internal sealed class FakeSizingStore : ISizingStore
{
    public List<SizingChangeRow> Settings { get; } = [];

    public List<BudgetChangeRow> Budgets { get; } = [];

    public int Appends { get; private set; }

    public ValueTask<SizingState> CurrentAsync(CancellationToken cancellationToken)
    {
        var settings = SizingSettings.Defaults;
        foreach (var row in Settings.OrderBy(r => r.Id))
        {
            settings = settings.With(row.Key, row.NewValue);
        }

        return ValueTask.FromResult(new SizingState(settings, Settings.Count == 0 ? 0 : Settings.Max(r => r.Id)));
    }

    public ValueTask<IReadOnlyList<SizingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SizingChangeRow>>([.. Settings.OrderByDescending(r => r.Id).Take(limit)]);

    public ValueTask<IReadOnlyDictionary<Guid, double>> BudgetsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, double>>(Budgets.GroupBy(b => b.BasketId).Select(g => g.OrderByDescending(r => r.Id).First()).Where(r => r.BudgetPct is not null).ToDictionary(r => r.BasketId, r => r.BudgetPct!.Value));

    public ValueTask<IReadOnlyList<BudgetChangeRow>> BudgetHistoryAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BudgetChangeRow>>([.. Budgets.OrderByDescending(r => r.Id).Take(limit)]);

    public ValueTask<bool> AppendSettingsAsync(IReadOnlyList<SizingChange> rows, long expectedVersion, CancellationToken cancellationToken)
    {
        if ((Settings.Count == 0 ? 0 : Settings.Max(r => r.Id)) != expectedVersion)
        {
            return ValueTask.FromResult(false);
        }

        foreach (var r in rows)
        {
            Settings.Add(new SizingChangeRow((Settings.Count == 0 ? 0 : Settings.Max(x => x.Id)) + 1, r.Key, r.OldValue, r.NewValue, r.Direction, r.Actor, DateTimeOffset.UnixEpoch, r.Hash));
        }

        Appends++;
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> AppendBudgetAsync(Guid basketId, double? expectedCurrentPct, double? budgetPct, SizingChange change, CancellationToken cancellationToken)
    {
        var current = Budgets.Where(b => b.BasketId == basketId).OrderByDescending(b => b.Id).FirstOrDefault()?.BudgetPct;
        if (current != expectedCurrentPct)
        {
            return ValueTask.FromResult(false);
        }

        Budgets.Add(new BudgetChangeRow((Budgets.Count == 0 ? 0 : Budgets.Max(b => b.Id)) + 1, basketId, budgetPct, change.Direction, change.Actor, DateTimeOffset.UnixEpoch, change.Hash));
        Appends++;
        return ValueTask.FromResult(true);
    }
}
