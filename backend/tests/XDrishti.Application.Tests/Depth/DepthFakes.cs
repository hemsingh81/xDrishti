using System.Runtime.CompilerServices;
using System.Threading.Channels;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Depth;

namespace XDrishti.Application.Tests.Depth;

/// <summary>A depth connection the test feeds by hand.</summary>
internal sealed class FakeDepthConnection : IDepthFeedConnection
{
    private readonly Channel<DepthPacket> _packets = Channel.CreateUnbounded<DepthPacket>();

    public List<IReadOnlyList<int>> Subscriptions { get; } = [];

    public bool Disposed { get; private set; }

    public long DroppedPackets => 0;

    public string EndReason { get; private set; } = string.Empty;

    public void Push(DepthPacket packet) => _packets.Writer.TryWrite(packet);

    /// <summary>The server closes the connection.</summary>
    public void Close()
    {
        EndReason = "closed by the server";
        _packets.Writer.TryComplete();
    }

    public ValueTask SubscribeAsync(IReadOnlyList<int> securityIds, CancellationToken cancellationToken)
    {
        Subscriptions.Add(securityIds);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<DepthPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
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

internal sealed class FakeDepthConnector : IDepthFeedConnector
{
    public List<FakeDepthConnection> Connections { get; } = [];

    /// <summary>Results to hand out before the default (a new connection each time).</summary>
    public Queue<DepthConnectResult> Scripted { get; } = new();

    public int Calls { get; private set; }

    public ValueTask<DepthConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        Calls++;
        if (Scripted.TryDequeue(out var scripted))
        {
            return ValueTask.FromResult(scripted);
        }

        var connection = new FakeDepthConnection();
        Connections.Add(connection);
        return ValueTask.FromResult(DepthConnectResult.Connected(connection));
    }
}

internal sealed class FakeDepthStore : IDepthStore
{
    private readonly Lock _gate = new();

    public List<DepthSnapshot> Rows { get; } = [];

    public int Appends { get; private set; }

    /// <summary>The next appends that fail (a database that is away).</summary>
    public int FailAppends { get; set; }

    public long Stored { get; set; }

    public long? Free { get; set; } = 500L * DepthSettings.Gb;

    public Dictionary<DateOnly, List<DepthSetEntry>> Sets { get; } = [];

    public List<DepthSessionRecord> Sessions { get; } = [];

    public ValueTask<int> AppendAsync(IReadOnlyList<DepthSnapshot> rows, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Appends++;
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

    public ValueTask<IReadOnlyList<DepthSetEntry>> LoadSetAsync(DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DepthSetEntry>>(Sets.TryGetValue(date, out var set) ? [.. set] : []);

    public ValueTask SaveSetAsync(DateOnly date, IReadOnlyList<DepthSetEntry> set, CancellationToken cancellationToken)
    {
        Sets.TryAdd(date, [.. set]);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RecordSessionAsync(DepthSessionRecord session, CancellationToken cancellationToken)
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

internal sealed class FakeDepthSwitchStore : IDepthSwitchStore
{
    public List<DepthSwitchRow> Rows { get; } = [];

    /// <summary>The read fails (a database that is away).</summary>
    public bool Fail { get; set; }

    public ValueTask<DepthSwitchRow?> CurrentAsync(CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("the database is away") : ValueTask.FromResult(Rows.Count > 0 ? Rows[^1] : null);

    public ValueTask<IReadOnlyList<DepthSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DepthSwitchRow>>([.. Enumerable.Reverse(Rows).Take(take)]);

    public ValueTask<DepthSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        var row = new DepthSwitchRow(Rows.Count + 1, enabled, actor, reason, DateTimeOffset.UnixEpoch);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }

    public void Set(bool enabled) => Rows.Add(new DepthSwitchRow(Rows.Count + 1, enabled, "owner:test@example.invalid", "test", DateTimeOffset.UnixEpoch));
}

internal sealed class FakeDepthHeartbeats : IDepthHeartbeats
{
    public List<(string State, DepthHeartbeat Details)> Reports { get; } = [];

    public DepthStatus? Status { get; set; }

    public ValueTask ReportAsync(string state, DepthHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        Reports.Add((state, details));
        return ValueTask.CompletedTask;
    }

    public ValueTask<DepthStatus?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Status);
}

internal sealed class FakeDepthUniverse : IDepthUniverse
{
    public List<DepthTurnover> Rows { get; } = [];

    public int Calls { get; private set; }

    public ValueTask<IReadOnlyList<DepthTurnover>> MostLiquidAsync(DateOnly before, int take, CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult<IReadOnlyList<DepthTurnover>>([.. Rows.Take(take)]);
    }
}

internal sealed class FakeArmedTickets : IArmedTicketReader
{
    public List<ArmedTicket> Tickets { get; } = [];

    /// <summary>The read fails (a database that is away).</summary>
    public bool Fail { get; set; }

    public ValueTask<IReadOnlyList<ArmedTicket>> ListAsync(DateOnly forDate, CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("the database is away") : ValueTask.FromResult<IReadOnlyList<ArmedTicket>>([.. Tickets]);
}

internal sealed class FakeSecurities : IInstrumentSecurities
{
    public Dictionary<string, InstrumentSecurity> Known { get; } = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyDictionary<string, InstrumentSecurity>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, InstrumentSecurity>>(Known.Where(k => canonicalSymbols.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal));
}

/// <summary>A session the test controls: it runs until the test finishes it (or it is cancelled, which ends it as stopped).</summary>
internal sealed class FakeDepthSession : IDepthSession
{
    private TaskCompletionSource<DepthSessionResult>? _current;

    public string State { get; set; } = DepthStates.Recording;

    public DepthRunStats Stats { get; set; } = new(0, 0, 0, 0, 0);

    public int Starts { get; private set; }

    public IReadOnlyList<DepthSetEntry>? LastSet { get; private set; }

    public bool Running => _current is { Task.IsCompleted: false };

    public Task<DepthSessionResult> RunAsync(IReadOnlyList<DepthSetEntry> set, CancellationToken cancellationToken)
    {
        Starts++;
        LastSet = set;
        var current = new TaskCompletionSource<DepthSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = current;
        cancellationToken.Register(() => current.TrySetResult(new DepthSessionResult(DepthSessionEnd.Stopped, null, Stats)));
        return current.Task;
    }

    public void Finish(DepthSessionEnd end, string? fingerprint = null, DepthRunStats? stats = null) => _current!.TrySetResult(new DepthSessionResult(end, fingerprint, stats ?? Stats));

    public void Crash() => _current!.TrySetException(new InvalidOperationException("boom"));
}
