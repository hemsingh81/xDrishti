using System.Text;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>
/// The calendar rows in memory, with the unit of work and the audit trail: added rows show up in the store after a save and a refused save drops them (as the database
/// does); rows changed in place stay changed, so collision tests use operations that add or remove rows.
/// </summary>
internal sealed class InMemoryCalendarData : ITradingDayStore, IMarketUnitOfWork, IAccountEventLog
{
    private readonly List<TradingDay> _pendingAdds = [];
    private readonly List<TradingDay> _pendingRemoves = [];
    private readonly List<AccountEvent> _pendingEvents = [];

    public List<TradingDay> Days { get; } = [];

    public List<AccountEvent> Events { get; } = [];

    /// <summary>The next saves that are refused, each one first running what a concurrent request would have done.</summary>
    public Queue<Action> Collisions { get; } = new();

    public int Saves { get; private set; }

    public ValueTask<IReadOnlyList<TradingDay>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TradingDay>>([.. Days.OrderBy(d => d.Id)]);

    public ValueTask<TradingDay?> FindAsync(DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult(Days.FirstOrDefault(d => d.Id == date));

    public ValueTask<int> CountProposedAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Days.Count(d => d.Status == CalendarStatus.Proposed || d.HasPendingProposal));

    public void Add(TradingDay day) => _pendingAdds.Add(day);

    public void Remove(TradingDay day) => _pendingRemoves.Add(day);

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
            _pendingRemoves.Clear();
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
        Days.AddRange(_pendingAdds);
        foreach (var removed in _pendingRemoves)
        {
            Days.Remove(removed);
        }

        Events.AddRange(_pendingEvents);
        _pendingAdds.Clear();
        _pendingRemoves.Clear();
        _pendingEvents.Clear();
    }
}

/// <summary>A fixed trading calendar (the snapshot the handlers would read).</summary>
internal sealed class FixedCalendar(TradingCalendar? calendar = null) : ITradingCalendar
{
    public TradingCalendar Calendar { get; set; } = calendar ?? new TradingCalendar();

    public int Invalidations { get; private set; }

    public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Calendar);

    public void Invalidate() => Invalidations++;
}

/// <summary>NSE's holiday list as a canned answer.</summary>
internal sealed class FakeHolidaySource : IHolidaySource
{
    public string? Json { get; set; }

    public string? Failure { get; set; }

    public int Calls { get; private set; }

    public ValueTask<SourceDownload> OpenHolidaysAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(Failure is not null || Json is null
            ? SourceDownload.Failed(Failure ?? "NSE's holiday list answered HTTP 403.")
            : SourceDownload.WithContent(new MemoryStream(Encoding.UTF8.GetBytes(Json)), SourceStamp()));
    }

    /// <summary>The shape NSE serves: segments with entries that have a date and a description.</summary>
    public static string Sample(params (string Date, string Description)[] cm) =>
        "{\"CM\":[" + string.Join(',', cm.Select(h => $"{{\"Sequence\":1,\"tradingDate\":\"{h.Date}\",\"weekDay\":\"x\",\"description\":\"{h.Description}\",\"morning_session\":\"\",\"evening_session\":\"\"}}")) + "],\"FO\":[]}";

    private static XDrishti.Domain.Market.SourceStamp SourceStamp() => XDrishti.Domain.Market.SourceStamp.None;
}

/// <summary>The stored bars' view for the observed proposals, canned.</summary>
internal sealed class FakeObservedDays : IObservedDays
{
    public DayPresence Presence { get; set; } = new(false, [], []);

    public Dictionary<DateOnly, ObservedWindow> Windows { get; } = [];

    public List<IReadOnlyCollection<DateOnly>> WindowRequests { get; } = [];

    public ValueTask<DayPresence> ReadPresenceAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Presence);

    public ValueTask<IReadOnlyDictionary<DateOnly, ObservedWindow>> ReadWindowsAsync(IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken)
    {
        WindowRequests.Add(dates);
        return ValueTask.FromResult<IReadOnlyDictionary<DateOnly, ObservedWindow>>(Windows.Where(w => dates.Contains(w.Key)).ToDictionary(w => w.Key, w => w.Value));
    }
}

/// <summary>Stands in for the calendar upkeep of a data job and records that it ran.</summary>
internal sealed class FakeCalendarUpkeep : ICalendarUpkeep
{
    public int Runs { get; private set; }

    public Func<JobReporter, ValueTask>? Action { get; set; }

    public async ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        Runs++;
        if (Action is { } action)
        {
            await action(reporter);
        }
    }
}
