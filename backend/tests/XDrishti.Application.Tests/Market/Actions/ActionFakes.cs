using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>
/// The corporate action rows in memory, with the unit of work and the audit trail: added rows show up after a save and a refused save drops them (as the database does); rows changed in place stay
/// changed, so collision tests use operations that add rows.
/// </summary>
internal sealed class InMemoryActionData : ICorporateActionStore, ICorporateActionQueries, IMarketUnitOfWork, IAccountEventLog
{
    private readonly List<CorporateAction> _pendingAdds = [];
    private readonly List<AccountEvent> _pendingEvents = [];

    public List<CorporateAction> Actions { get; } = [];

    public List<AccountEvent> Events { get; } = [];

    public Dictionary<Guid, ActionInstrument> Instruments { get; } = [];

    /// <summary>The date the loaded CSV files of an instrument reach, set by a test.</summary>
    public Dictionary<Guid, DateOnly> CsvThrough { get; } = [];

    /// <summary>The next saves that are refused, each one first running what a concurrent request would have done.</summary>
    public Queue<Action> Collisions { get; } = new();

    public int Saves { get; private set; }

    public ActionInstrument AddInstrument(string ticker)
    {
        var instrument = new ActionInstrument(Guid.NewGuid(), "NSE:" + ticker, ticker + " Ltd");
        Instruments[instrument.Id] = instrument;
        return instrument;
    }

    public ValueTask<CorporateAction?> FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Actions.FirstOrDefault(a => a.Id == id));

    public ValueTask<bool> ExistsAsync(Guid instrumentId, DateOnly exDate, CorporateActionKind kind, Guid? except, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Actions.Any(a => a.InstrumentId == instrumentId && a.ExDate == exDate && a.Kind == kind && a.Id != except));

    public ValueTask<IReadOnlyList<CorporateAction>> ListNotIgnoredAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<CorporateAction>>([.. Actions.Where(a => instrumentIds.Contains(a.InstrumentId) && a.Status != CorporateActionStatus.Ignored)]);

    public ValueTask<IReadOnlyDictionary<Guid, DateOnly>> ListCsvAdjustedThroughAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, DateOnly>>(CsvThrough.Where(c => instrumentIds.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value));

    public void Add(CorporateAction action) => _pendingAdds.Add(action);

    public ValueTask<int> AddSuggestionsAsync(IReadOnlyCollection<CorporateAction> suggestions, CancellationToken cancellationToken)
    {
        var stored = 0;
        foreach (var suggestion in suggestions.Where(s => !Actions.Any(a => a.InstrumentId == s.InstrumentId && a.ExDate == s.ExDate && a.Kind == s.Kind)))
        {
            Actions.Add(suggestion);
            stored++;
        }

        return ValueTask.FromResult(stored);
    }

    public ValueTask<CorporateActionPage> ListAsync(CorporateActionSearch search, CancellationToken cancellationToken)
    {
        var rows = Actions
            .Where(a => (search.Status is null || a.Status == search.Status) && (search.InstrumentId is null || a.InstrumentId == search.InstrumentId))
            .OrderByDescending(a => a.ExDate)
            .ToList();
        var page = rows.Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(a => CorporateActionView.From(a, Instruments[a.InstrumentId].Symbol, Instruments[a.InstrumentId].Name))
            .ToList();
        return ValueTask.FromResult(new CorporateActionPage(page, search.Page, search.PageSize, rows.Count, Actions.Count(a => a.Status == CorporateActionStatus.Suggested)));
    }

    public ValueTask<PendingActions> GetPendingAsync(CancellationToken cancellationToken)
    {
        var suggested = Actions.Where(a => a.Status == CorporateActionStatus.Suggested).ToList();
        return ValueTask.FromResult(new PendingActions(suggested.Count, suggested.Count == 0 ? null : suggested.Max(a => a.CreatedAt)));
    }

    public ValueTask<IReadOnlyList<AppliedAction>> ListAppliedAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AppliedAction>>([.. Actions
            .Where(a => a.InstrumentId == instrumentId && a.Status == CorporateActionStatus.Applied)
            .OrderBy(a => a.ExDate)
            .Select(a => new AppliedAction(a.ExDate, a.Kind, a.Numerator, a.Denominator, a.Factor))]);

    public ValueTask<IReadOnlySet<DateOnly>> ListExDatesAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<DateOnly>>(Actions.Where(a => a.InstrumentId == instrumentId).SelectMany(a => a.DetectedExDate is { } detected ? [a.ExDate, detected] : new[] { a.ExDate }).ToHashSet());

    public ValueTask<IReadOnlySet<ActionKey>> ListKeysAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<ActionKey>>(Actions.Where(a => instrumentIds.Contains(a.InstrumentId)).Select(a => new ActionKey(a.InstrumentId, a.ExDate, a.Kind)).ToHashSet());

    public ValueTask<ActionInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Instruments.TryGetValue(id, out var instrument) ? instrument : null);

    public void Record(AccountEvent accountEvent) => _pendingEvents.Add(accountEvent);

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);

    public ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        Commit();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        if (Collisions.TryDequeue(out var concurrentRequest))
        {
            Saves++;
            _pendingAdds.Clear();
            _pendingEvents.Clear();
            concurrentRequest();
            return ValueTask.FromResult(false);
        }

        Commit();
        return ValueTask.FromResult(true);
    }

    private void Commit()
    {
        Saves++;
        Actions.AddRange(_pendingAdds);
        Events.AddRange(_pendingEvents);
        _pendingAdds.Clear();
        _pendingEvents.Clear();
    }
}

/// <summary>Day bars per instrument, in memory.</summary>
internal sealed class FakeDailySeries : IDailySeries
{
    public Dictionary<Guid, (string Symbol, List<DailyBar> Bars)> Series { get; } = [];

    public Guid Add(string symbol, IEnumerable<DailyBar> bars)
    {
        var id = Guid.NewGuid();
        Series[id] = (symbol, [.. bars]);
        return id;
    }

    public ValueTask<IReadOnlyList<SeriesInstrument>> ListInstrumentsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SeriesInstrument>>([.. Series.Select(s => new SeriesInstrument(s.Key, s.Value.Symbol)).OrderBy(s => s.Symbol, StringComparer.Ordinal)]);

    public ValueTask<IReadOnlyList<DailyBar>> ReadAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DailyBar>>(Series.TryGetValue(instrumentId, out var s) ? [.. s.Bars.Where(b => b.Date >= from).OrderBy(b => b.Date)] : []);
}

/// <summary>Builds day series for detection tests: a calm series with a steady volume, with events written into it.</summary>
internal static class DaySeries
{
    public static readonly DateOnly Start = new(2026, 1, 5);

    /// <summary>
    /// <paramref name="days"/> weekdays from <see cref="Start"/>, the close drifting ±0.4 % around 1000, the open at the previous close, volume 100000 ± 5 %.
    /// <paramref name="shape"/> may change a day (index, bar) afterwards.
    /// </summary>
    public static List<DailyBar> Calm(int days, Func<int, DailyBar, DailyBar>? shape = null)
    {
        var bars = new List<DailyBar>(days);
        var date = Start;
        var close = 1000.0;
        for (var i = 0; i < days; i++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            var open = close;
            close = open * (1 + (((i * 7) % 9) - 4) / 1000.0);
            var bar = new DailyBar(date, open, Math.Max(open, close) * 1.002, Math.Min(open, close) * 0.998, close, 100_000 + ((i * 13) % 11 - 5) * 1000L);
            bars.Add(shape?.Invoke(i, bar) ?? bar);
            close = bars[^1].Close;
            date = date.AddDays(1);
        }

        return bars;
    }

    /// <summary>A calm series with a gap at day <paramref name="eventDay"/>: it opens at <paramref name="ratio"/> times the previous close on <paramref name="volumeMultiple"/> times the usual volume, and the days after follow the new level.</summary>
    public static List<DailyBar> WithGap(int days, int eventDay, double ratio, double volumeMultiple) =>
        Calm(days, (i, bar) => i == eventDay ? Gap(bar, bar.Open, ratio, volumeMultiple) : bar);

    /// <summary>A day that opens at <paramref name="ratio"/> times the previous close and holds there, on <paramref name="volumeMultiple"/> times the usual volume.</summary>
    public static DailyBar Gap(DailyBar bar, double previousClose, double ratio, double volumeMultiple)
    {
        var level = previousClose * ratio;
        return bar with { Open = level, High = level * 1.004, Low = level * 0.996, Close = level * 1.001, Volume = (long)(100_000 * volumeMultiple) };
    }
}
