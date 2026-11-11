using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Tests.Ticks;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>The opening-minute recorder's connector over the same scripted connection as the tick tests; it counts the connection attempts.</summary>
internal sealed class FakeOpeningConnector : IOpeningBarsFeedConnector
{
    public List<FakeTickConnection> Connections { get; } = [];

    public Queue<FeedConnectResult> Scripted { get; } = new();

    public int Calls { get; private set; }

    public ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        Calls++;
        if (Scripted.TryDequeue(out var scripted))
        {
            return ValueTask.FromResult(scripted);
        }

        var connection = new FakeTickConnection();
        Connections.Add(connection);
        return ValueTask.FromResult(FeedConnectResult.Connected(connection));
    }
}

/// <summary>An in-memory store with the real one's rules: a minute of an instrument is kept once, a second write is a duplicate.</summary>
internal sealed class FakeOpeningStore : IOpeningBarsStore
{
    private readonly Lock _gate = new();

    public List<MinuteBar> Bars { get; } = [];

    public List<(DateOnly Day, OpeningBarsEvent Event)> Events { get; } = [];

    public int FailAppends { get; set; }

    public int AppendCalls { get; private set; }

    public long? Free { get; set; } = 500L * 1024 * 1024 * 1024;

    public ValueTask<int> AppendAsync(IReadOnlyList<MinuteBar> bars, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            AppendCalls++;
            if (FailAppends > 0)
            {
                FailAppends--;
                throw new InvalidOperationException("the database is away");
            }

            var added = 0;
            foreach (var bar in bars.Where(b => !Bars.Any(x => x.InstrumentId == b.InstrumentId && x.MinuteStart == b.MinuteStart)))
            {
                Bars.Add(bar);
                added++;
            }

            return ValueTask.FromResult(added);
        }
    }

    public ValueTask AddEventAsync(DateOnly day, OpeningBarsEvent @event, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Events.Add((day, @event));
        }

        return ValueTask.CompletedTask;
    }

    public List<int> Prunes { get; } = [];

    public ValueTask<int> PruneAsync(int keepDays, CancellationToken cancellationToken)
    {
        Prunes.Add(keepDays);
        return ValueTask.FromResult(0);
    }

    public ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Free);
}

/// <summary>What the derived job reads: the day's live rows, a history and the day's events (the test fills them).</summary>
internal sealed class FakeOpeningQueries : IOpeningBarsQueries
{
    public List<MinuteBar> Day { get; } = [];

    public List<MinuteBar> History { get; } = [];

    public List<OpeningBarsEvent> Events { get; } = [];

    public ValueTask<IReadOnlyList<MinuteBar>> ReadDayAsync(DateOnly day, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MinuteBar>>([.. Day]);

    public ValueTask<IReadOnlyList<MinuteBar>> ReadHistoryAsync(IReadOnlyCollection<Guid> instruments, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<MinuteBar>>([.. History.Where(b => instruments.Contains(b.InstrumentId))]);

    public ValueTask<IReadOnlyList<OpeningBarsEvent>> ReadEventsAsync(DateOnly day, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<OpeningBarsEvent>>([.. Events]);
}

internal sealed class FakeOpeningSwitch : IOpeningBarsSwitchStore
{
    public bool On { get; set; }

    public ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(On);

    public List<OpeningBarsSwitchRow> Rows { get; } = [];

    public ValueTask<IReadOnlyList<OpeningBarsSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<OpeningBarsSwitchRow>>([.. Rows.OrderByDescending(r => r.Id).Take(take)]);

    public ValueTask<OpeningBarsSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        On = enabled;
        var row = new OpeningBarsSwitchRow(Rows.Count + 1, enabled, actor, reason, DateTimeOffset.UnixEpoch);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }
}
