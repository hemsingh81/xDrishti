using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Selection;

/// <summary>An in-memory <see cref="IBasketSettingsStore"/> with the real rules: insert-only, newest row per key wins, a stale version is refused.</summary>
internal sealed class FakeBasketSettingsStore(BasketSettings? initial = null) : IBasketSettingsStore
{
    public List<BasketSettingChangeRow> Rows { get; } = [];

    public int Appends { get; private set; }

    private BasketSettings Seed { get; } = initial ?? BasketSettings.Defaults;

    public ValueTask<BasketSettingsState> CurrentAsync(CancellationToken cancellationToken)
    {
        var settings = Seed;
        foreach (var row in Rows.OrderBy(r => r.Id))
        {
            settings = settings.With(row.Key, row.NewValue);
        }

        return ValueTask.FromResult(new BasketSettingsState(settings, Rows.Count == 0 ? 0 : Rows.Max(r => r.Id)));
    }

    public ValueTask<IReadOnlyList<BasketSettingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BasketSettingChangeRow>>([.. Rows.OrderByDescending(r => r.Id).Take(limit)]);

    public ValueTask<bool> AppendAsync(IReadOnlyList<BasketSettingChange> rows, long expectedVersion, CancellationToken cancellationToken)
    {
        if ((Rows.Count == 0 ? 0 : Rows.Max(r => r.Id)) != expectedVersion)
        {
            return ValueTask.FromResult(false);
        }

        foreach (var r in rows)
        {
            Rows.Add(new BasketSettingChangeRow((Rows.Count == 0 ? 0 : Rows.Max(x => x.Id)) + 1, r.Key, r.OldValue, r.NewValue, r.Direction, r.Actor, DateTimeOffset.UnixEpoch, r.Hash));
        }

        Appends++;
        return ValueTask.FromResult(true);
    }
}
