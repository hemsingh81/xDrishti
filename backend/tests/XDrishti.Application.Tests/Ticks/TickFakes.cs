using System.Runtime.CompilerServices;
using System.Threading.Channels;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Application.Ticks;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>A feed connection the test feeds by hand; it records each subscribe message with its mode.</summary>
internal sealed class FakeTickConnection : ILiveFeedConnection
{
    private readonly Channel<FeedPacket> _packets = Channel.CreateUnbounded<FeedPacket>();

    public List<(FeedMode Mode, IReadOnlyList<FeedSecurity> Securities)> Subscriptions { get; } = [];

    public int Unsubscribes { get; private set; }

    public bool Disposed { get; private set; }

    public long DroppedPackets => 0;

    public string EndReason { get; private set; } = string.Empty;

    public void Push(FeedPacket packet) => _packets.Writer.TryWrite(packet);

    public void Close()
    {
        EndReason = "closed by the server";
        _packets.Writer.TryComplete();
    }

    public ValueTask SubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken)
    {
        Subscriptions.Add((mode, [.. securities]));
        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken)
    {
        Unsubscribes++;
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<FeedPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            bool more;
            try
            {
                more = await _packets.Reader.WaitToReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
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

internal sealed class FakeTickConnector : ITickFeedConnector
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

internal sealed class FakeTickStore : ITickStore
{
    private readonly Lock _gate = new();

    public List<TradeTick> Rows { get; } = [];

    public int FailAppends { get; set; }

    public long Stored { get; set; }

    public long? Free { get; set; } = 500L * TickSettings.Gb;

    /// <summary>The depth recorder's stored sets (read only here).</summary>
    public Dictionary<DateOnly, List<TickSetEntry>> DepthSets { get; } = [];

    public Dictionary<DateOnly, List<TickSetEntry>> Sets { get; } = [];

    public List<TickSessionRecord> Sessions { get; } = [];

    public ValueTask<int> AppendAsync(IReadOnlyList<TradeTick> rows, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (FailAppends > 0)
            {
                FailAppends--;
                throw new InvalidOperationException("the database is away");
            }

            Rows.AddRange(rows);
        }

        return ValueTask.FromResult(rows.Count);
    }

    public ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Stored);

    public ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Free);

    public ValueTask<IReadOnlyList<TickSetEntry>> LoadDepthSetAsync(DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TickSetEntry>>(DepthSets.TryGetValue(date, out var set) ? [.. set] : []);

    public ValueTask<IReadOnlyList<TickSetEntry>> LoadSetAsync(DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TickSetEntry>>(Sets.TryGetValue(date, out var set) ? [.. set] : []);

    public ValueTask SaveSetAsync(DateOnly date, IReadOnlyList<TickSetEntry> set, CancellationToken cancellationToken)
    {
        Sets.TryAdd(date, [.. set]);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RecordSessionAsync(TickSessionRecord session, CancellationToken cancellationToken)
    {
        if (Sessions.Any(s => s.Date == session.Date))
        {
            return ValueTask.FromResult(false);
        }

        Sessions.Add(session);
        return ValueTask.FromResult(true);
    }

    public ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DateOnly>>([.. Sets.Keys.Where(d => Sessions.All(s => s.Date != d)).Order()]);
}

internal sealed class FakeTickSwitchStore : ITickSwitchStore
{
    public List<TickSwitchRow> Rows { get; } = [];

    public bool Fail { get; set; }

    public ValueTask<TickSwitchRow?> CurrentAsync(CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("the database is away") : ValueTask.FromResult(Rows.Count > 0 ? Rows[^1] : null);

    public ValueTask<IReadOnlyList<TickSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TickSwitchRow>>([.. Enumerable.Reverse(Rows).Take(take)]);

    public ValueTask<TickSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        var row = new TickSwitchRow(Rows.Count + 1, enabled, actor, reason, DateTimeOffset.UnixEpoch);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }

    public void Set(bool enabled) => Rows.Add(new TickSwitchRow(Rows.Count + 1, enabled, "owner:test@example.invalid", "test", DateTimeOffset.UnixEpoch));
}

internal sealed class FakeTickHeartbeats : ITickHeartbeats
{
    public List<(string State, TickHeartbeat Details)> Reports { get; } = [];

    public TickStatus? Status { get; set; }

    public ValueTask ReportAsync(string state, TickHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        Reports.Add((state, details));
        return ValueTask.CompletedTask;
    }

    public ValueTask<TickStatus?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Status);
}

/// <summary>A tick session the test controls: it runs until the test finishes it (or it is cancelled, which ends it as stopped).</summary>
internal sealed class FakeTickSession : ITickSession
{
    private TaskCompletionSource<TickSessionResult>? _current;

    public string State { get; set; } = DepthStates.Recording;

    public TickRunStats Stats { get; set; } = new(0, 0, 0, 0, 0, 0);

    public int Starts { get; private set; }

    public IReadOnlyList<TickSetEntry>? LastSet { get; private set; }

    public Task<TickSessionResult> RunAsync(IReadOnlyList<TickSetEntry> set, CancellationToken cancellationToken)
    {
        Starts++;
        LastSet = set;
        var current = new TaskCompletionSource<TickSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = current;
        cancellationToken.Register(() => current.TrySetResult(new TickSessionResult(DepthSessionEnd.Stopped, null, Stats)));
        return current.Task;
    }

    public void Finish(DepthSessionEnd end, string? fingerprint = null, TickRunStats? stats = null) => _current!.TrySetResult(new TickSessionResult(end, fingerprint, stats ?? Stats));

    public void Crash() => _current!.TrySetException(new InvalidOperationException("boom"));
}

/// <summary>Constructed quote packets (the layout is Dhan's documented one, UNVERIFIED until the pilot).</summary>
internal static class TickTestData
{
    public static readonly Guid Reliance = Guid.Parse("00000000-0000-0000-0000-000000000011");

    public static readonly Guid Tcs = Guid.Parse("00000000-0000-0000-0000-000000000012");

    public const int RelianceId = 2885;

    public const int TcsId = 11536;

    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>10:00 India time on Monday 2026-10-12.</summary>
    public static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, Ist);

    public static FeedPacket Quote(long volume, decimal price = 100.05m, int? ltq = 10, DateTimeOffset? ltt = null, int securityId = RelianceId) =>
        new(FeedPacketKind.Quote, new FeedSecurity(FeedSegments.NseEquity, securityId), price, ltt ?? T0, volume, LastQuantity: ltq);

    public static IReadOnlyDictionary<int, Guid> Set() => new Dictionary<int, Guid> { [RelianceId] = Reliance, [TcsId] = Tcs };
}
