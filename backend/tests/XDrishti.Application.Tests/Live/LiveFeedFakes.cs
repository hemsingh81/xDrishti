using System.Runtime.CompilerServices;
using System.Threading.Channels;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Live;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Live;

/// <summary>An open connection in memory: packets are pushed in by the test, subscriptions are recorded, closing it ends the stream.</summary>
internal sealed class FakeFeedConnection : ILiveFeedConnection
{
    private readonly Channel<FeedPacket> _packets = Channel.CreateUnbounded<FeedPacket>();
    private readonly object _gate = new();
    private readonly List<(FeedMode Mode, FeedSecurity[] Securities)> _subscribed = [];
    private readonly List<(FeedMode Mode, FeedSecurity[] Securities)> _unsubscribed = [];

    public long DroppedPackets { get; set; }

    public string EndReason { get; private set; } = string.Empty;

    public bool Disposed { get; private set; }

    public IReadOnlyList<(FeedMode Mode, FeedSecurity[] Securities)> Subscribed
    {
        get
        {
            lock (_gate)
            {
                return [.. _subscribed];
            }
        }
    }

    public IReadOnlyList<(FeedMode Mode, FeedSecurity[] Securities)> Unsubscribed
    {
        get
        {
            lock (_gate)
            {
                return [.. _unsubscribed];
            }
        }
    }

    public void Push(FeedPacket packet) => _packets.Writer.TryWrite(packet);

    /// <summary>The server closes the connection.</summary>
    public void Close(string reason = "closed by the server")
    {
        EndReason = reason;
        _packets.Writer.TryComplete();
    }

    public ValueTask SubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken)
    {
        securities.Count.ShouldBeLessThanOrEqualTo(ILiveFeedConnection.MaxBatch);
        lock (_gate)
        {
            _subscribed.Add((mode, [.. securities]));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken)
    {
        securities.Count.ShouldBeLessThanOrEqualTo(ILiveFeedConnection.MaxBatch);
        lock (_gate)
        {
            _unsubscribed.Add((mode, [.. securities]));
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<FeedPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var more = false;
            try
            {
                more = await _packets.Reader.WaitToReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // like the real connection, a cancelled read just ends
            }

            if (!more)
            {
                yield break;
            }

            while (_packets.Reader.TryRead(out var packet))
            {
                yield return packet;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>The connector in memory: answers each attempt from a queue (a new connection when the queue is empty), counts the attempts and the connections that are open at the same time.</summary>
internal sealed class FakeFeedConnector(TimeProvider clock) : ILiveFeedConnector
{
    private readonly object _gate = new();
    private readonly Queue<FeedConnectResult> _answers = new();
    private readonly List<DateTimeOffset> _attempts = [];

    public List<FakeFeedConnection> Connections { get; } = [];

    /// <summary>When set, every attempt not answered from the queue fails with this kind.</summary>
    public FeedConnectFailureKind? AlwaysFail { get; set; }

    public int Attempts
    {
        get
        {
            lock (_gate)
            {
                return _attempts.Count;
            }
        }
    }

    public IReadOnlyList<DateTimeOffset> AttemptTimes
    {
        get
        {
            lock (_gate)
            {
                return [.. _attempts];
            }
        }
    }

    public int MaxOpen { get; private set; }

    /// <summary>Thrown by the next attempt (then cleared): a connector that breaks its promise not to throw.</summary>
    public Exception? ThrowNext { get; set; }

    public void Enqueue(FeedConnectResult result)
    {
        lock (_gate)
        {
            _answers.Enqueue(result);
        }
    }

    public FakeFeedConnection? Connection(int index)
    {
        lock (_gate)
        {
            return index < Connections.Count ? Connections[index] : null;
        }
    }

    public ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _attempts.Add(clock.GetUtcNow());
            if (ThrowNext is { } failure)
            {
                ThrowNext = null;
                throw failure;
            }

            if (_answers.TryDequeue(out var queued))
            {
                return ValueTask.FromResult(queued);
            }

            if (AlwaysFail is { } kind)
            {
                return ValueTask.FromResult(FeedConnectResult.Failed(kind, "fake failure"));
            }

            var connection = new FakeFeedConnection();
            Connections.Add(connection);
            MaxOpen = Math.Max(MaxOpen, Connections.Count(c => !c.Disposed));
            return ValueTask.FromResult(FeedConnectResult.Connected(connection));
        }
    }
}

/// <summary>The price sink in memory.</summary>
internal sealed class FakeLiveFlush : ILiveFlush
{
    private readonly object _gate = new();
    private readonly List<LivePrice> _added = [];

    public int Resets { get; private set; }

    public int Flushes { get; private set; }

    public IReadOnlyList<LivePrice> Added
    {
        get
        {
            lock (_gate)
            {
                return [.. _added];
            }
        }
    }

    public void Add(LivePrice tick)
    {
        lock (_gate)
        {
            _added.Add(tick);
        }
    }

    public void Reset()
    {
        Resets++;
        lock (_gate)
        {
            _added.Clear();
        }
    }

    public ValueTask<LiveFlushResult> FlushAsync(CancellationToken cancellationToken)
    {
        Flushes++;
        return ValueTask.FromResult(LiveFlushResult.Nothing());
    }
}

/// <summary>The <c>active_set_changed</c> listener: the test raises the notification.</summary>
internal sealed class FakeActiveSetChanges : IActiveSetChanges
{
    private Action? _changed;

    public bool Watching => _changed is not null;

    public void Raise() => _changed?.Invoke();

    public async Task WatchAsync(Action changed, CancellationToken cancellationToken)
    {
        _changed = changed;
        var done = new TaskCompletionSource();
        await using var registration = cancellationToken.Register(() => done.TrySetResult());
        await done.Task;
    }
}

/// <summary>The Data account's credentials: a token that can be changed or taken away.</summary>
internal sealed class FakeCredentials : IDataAccountCredentials
{
    public string? Token { get; set; } = "token-one";

    public string ClientId { get; set; } = "1000000001";

    public ValueTask<DataAccountCredentials?> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        if (Token is null)
        {
            return ValueTask.FromResult<DataAccountCredentials?>(null);
        }

        SecretValue.TryCreate(Token, out var secret, out _);
        return ValueTask.FromResult<DataAccountCredentials?>(new DataAccountCredentials(secret!, ClientId));
    }
}

/// <summary>A provider whose candidates the test changes.</summary>
internal sealed class FakeActiveSetProvider : IActiveSetProvider
{
    public List<ActiveSetCandidate> Candidates { get; } = [];

    public Exception? Fail { get; set; }

    public ValueTask<IReadOnlyList<ActiveSetCandidate>> ListAsync(CancellationToken cancellationToken) =>
        Fail is { } failure ? throw failure : ValueTask.FromResult<IReadOnlyList<ActiveSetCandidate>>([.. Candidates]);
}

internal static class FeedTestData
{
    public static readonly Guid Reliance = new("00000000-0000-0000-0000-000000000001");

    public static readonly Guid Infosys = new("00000000-0000-0000-0000-000000000002");

    public static readonly Guid Nifty = new("00000000-0000-0000-0000-0000000000a1");

    public static readonly Guid Vix = new("00000000-0000-0000-0000-0000000000a2");

    public static ActiveSetCandidate Holding(Guid id, string securityId, decimal value) => new(id, FeedSegments.NseEquity, securityId, FeedMode.Quote, ActiveSetReason.Holding, value);

    public static ActiveSetCandidate Alert(Guid id, string securityId) => new(id, FeedSegments.NseEquity, securityId, FeedMode.Ticker, ActiveSetReason.Alert);

    public static ActiveSetCandidate Index(Guid id, string securityId) => new(id, FeedSegments.Index, securityId, FeedMode.Ticker, ActiveSetReason.Index);

    public static FeedPacket Tick(string segment, int securityId, decimal price, DateTimeOffset at, FeedPacketKind kind = FeedPacketKind.Ticker) =>
        new(kind, new FeedSecurity(segment, securityId), price, at);
}
