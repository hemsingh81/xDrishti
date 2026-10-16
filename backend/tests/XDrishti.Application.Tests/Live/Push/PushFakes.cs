using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Tests.Live.Push;

internal sealed class FakeLiveClients : ILiveClients
{
    public List<(string Connection, PricesChanged Message)> Prices { get; } = [];

    public List<(string Connection, LiveFeedStatus Message)> Statuses { get; } = [];

    public List<string> Disconnected { get; } = [];

    /// <summary>Connections a send to which throws (they have gone).</summary>
    public HashSet<string> Gone { get; } = [];

    public IEnumerable<PricesChanged> PricesOf(string connection) => Prices.Where(p => p.Connection == connection).Select(p => p.Message);

    public ValueTask SendPricesAsync(string connectionId, PricesChanged message, CancellationToken cancellationToken)
    {
        if (Gone.Contains(connectionId))
        {
            throw new InvalidOperationException("gone");
        }

        Prices.Add((connectionId, message));
        return ValueTask.CompletedTask;
    }

    public ValueTask SendStatusAsync(string connectionId, LiveFeedStatus message, CancellationToken cancellationToken)
    {
        if (Gone.Contains(connectionId))
        {
            throw new InvalidOperationException("gone");
        }

        Statuses.Add((connectionId, message));
        return ValueTask.CompletedTask;
    }

    public void Disconnect(string connectionId) => Disconnected.Add(connectionId);
}

internal sealed class FakePriceReader : ILivePriceReader
{
    private readonly Channel<int> _called = Channel.CreateUnbounded<int>();

    public Dictionary<Guid, InstrumentPrice> Prices { get; } = [];

    public List<HashSet<Guid>> Requests { get; } = [];

    public List<DateTimeOffset> At { get; } = [];

    public TimeProvider? Clock { get; set; }

    public Exception? Failure { get; set; }

    public int Calls => Requests.Count;

    public void Set(Guid id, decimal price, DateTimeOffset at, string source = "live") => Prices[id] = new InstrumentPrice(id, price, at, source);

    public ValueTask<IReadOnlyList<InstrumentPrice>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        lock (Requests)
        {
            Requests.Add([.. instrumentIds]);
            At.Add(Clock?.GetUtcNow() ?? default);
        }

        _called.Writer.TryWrite(1);
        return ValueTask.FromResult<IReadOnlyList<InstrumentPrice>>([.. instrumentIds.Where(Prices.ContainsKey).Select(id => Prices[id])]);
    }

    /// <summary>Waits until the reader has been called <paramref name="calls"/> times in total.</summary>
    public async Task WaitForCallsAsync(int calls, CancellationToken cancellationToken)
    {
        while (Calls < calls)
        {
            await _called.Reader.ReadAsync(cancellationToken);
        }
    }
}

internal sealed class FakeScopeResolver : ILiveScopeResolver
{
    public List<(Guid? AccountId, Guid? BucketId, Guid[] HoldingIds)> Requests { get; } = [];

    public List<Guid> Result { get; set; } = [];

    public Exception? Failure { get; set; }

    public ValueTask<IReadOnlyList<Guid>> ResolveAsync(Guid? accountId, Guid? bucketId, IReadOnlyCollection<Guid> holdingIds, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Requests.Add((accountId, bucketId, [.. holdingIds]));
        return ValueTask.FromResult<IReadOnlyList<Guid>>([.. Result]);
    }
}

internal sealed class FakeStatusHandler : IQueryHandler<GetLiveStatusQuery, LiveFeedStatus>
{
    public LiveFeedStatus Status { get; set; } = new("connected", 12, null);

    public ValueTask<LiveFeedStatus> HandleAsync(GetLiveStatusQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(Status);
}

internal sealed class FakeEndedSessions : IQueryHandler<ListEndedSessionsQuery, IReadOnlyList<Guid>>
{
    public HashSet<Guid> Ended { get; } = [];

    public List<Guid[]> Asked { get; } = [];

    public ValueTask<IReadOnlyList<Guid>> HandleAsync(ListEndedSessionsQuery query, CancellationToken cancellationToken)
    {
        Asked.Add([.. query.SessionIds]);
        return ValueTask.FromResult<IReadOnlyList<Guid>>([.. query.SessionIds.Where(Ended.Contains)]);
    }
}

internal sealed class FakePriceChanges : ILivePriceChanges
{
    private readonly TaskCompletionSource<Action> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Action> Started => _started.Task;

    public async Task WatchAsync(Action changed, CancellationToken cancellationToken)
    {
        _started.TrySetResult(changed);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
    }
}

/// <summary>A fake clock that says which timers were asked for, so a test advances time only once the code under test is waiting for it.</summary>
internal sealed class TimerWatchingClock : FakeTimeProvider
{
    private readonly ConcurrentDictionary<TimeSpan, SemaphoreSlim> _created = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _created.GetOrAdd(dueTime, _ => new SemaphoreSlim(0)).Release();
        return timer;
    }

    /// <summary>Waits for one more timer with this due time to be created (each call consumes one).</summary>
    public Task WaitForTimerAsync(TimeSpan dueTime, CancellationToken cancellationToken) =>
        _created.GetOrAdd(dueTime, _ => new SemaphoreSlim(0)).WaitAsync(cancellationToken);
}

/// <summary>The coordinator with fakes for everything it reaches (a scope per step, as in the host).</summary>
internal sealed class PushWorld
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    public PushWorld(PushSettings? settings = null, TimerWatchingClock? clock = null)
    {
        Clock = clock ?? new TimerWatchingClock();
        Clock.SetUtcNow(Now);
        Reader.Clock = Clock;
        Settings = settings ?? PushSettings.Default;
        Provider = new Platform.Services.MiniProvider()
            .ScopedInstance<ILivePriceReader>(Reader)
            .ScopedInstance<ILiveScopeResolver>(Resolver)
            .ScopedInstance<IQueryHandler<GetLiveStatusQuery, LiveFeedStatus>>(StatusHandler)
            .ScopedInstance<IQueryHandler<ListEndedSessionsQuery, IReadOnlyList<Guid>>>(EndedSessions);
        Coordinator = new LivePushCoordinator(Provider, Clients, Changes, Settings, Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<LivePushCoordinator>.Instance);
    }

    public TimerWatchingClock Clock { get; }

    public PushSettings Settings { get; }

    public FakeLiveClients Clients { get; } = new();

    public FakePriceReader Reader { get; } = new();

    public FakeScopeResolver Resolver { get; } = new();

    public FakeStatusHandler StatusHandler { get; } = new();

    public FakeEndedSessions EndedSessions { get; } = new();

    public FakePriceChanges Changes { get; } = new();

    public Platform.Services.MiniProvider Provider { get; }

    public LivePushCoordinator Coordinator { get; }

    /// <summary>Connects <paramref name="connection"/> and subscribes it to <paramref name="instruments"/> (what the scope resolves to).</summary>
    public async Task<SubscribeAck> FollowAsync(string connection, Guid session, params Guid[] instruments)
    {
        await Coordinator.ConnectedAsync(connection, session, CancellationToken.None);
        Resolver.Result = [.. instruments];
        return await Coordinator.SubscribeAsync(connection, new LiveScope(null, null, null), CancellationToken.None);
    }
}
