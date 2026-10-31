using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Application.Live;

namespace XDrishti.Application.Ticks;

/// <summary>
/// The connection of the trade-tick recorder for one run (P7-16b criteria 1, 3, 4, 7 to 9): connect with the primary Data account's credentials through its own connector (the live feed's allowed endpoint, another socket), subscribe the day's set in
/// <b>quote mode only</b> in messages of at most 100, then stream. The receive loop numbers every packet (<c>rx_seq</c>), hands it to the <see cref="TickSampler"/> and queues the rows it makes; it never waits for the database. A maintenance loop
/// beside it writes the queue every <c>Ticks:FlushSeconds</c> (one transaction; a failed batch stays for the next flush; rows unwritten for more than 30 seconds are dropped oldest first and counted; the queue holds at most
/// <see cref="MaxPending"/>) and asks the disk guard every five minutes (<see cref="DepthDiskGuard"/>: the recorder's own cap, the shared free floor). A dropped connection is replaced after the back-off with the same set and every instrument's next
/// row is a baseline; more than 10 attempts in 5 minutes or a 805 end the run as flapping, a refused token or a 806 end it until the credentials change. The live feed and the depth recorder are untouched: another connection, other state, its own catch.
/// </summary>
internal sealed class TickRecorder(IServiceScopeFactory scopes, ITickFeedConnector connector, TickSettings settings, TimeProvider clock, ILogger<TickRecorder> logger) : ITickSession
{
    private readonly int _maxPending = MaxPending;

    /// <summary>Rows unwritten for longer than this are dropped, oldest first.</summary>
    public static readonly TimeSpan MaxUnwritten = TimeSpan.FromSeconds(30);

    /// <summary>The most rows waiting for the database; more are dropped and counted.</summary>
    public const int MaxPending = 50_000;

    /// <summary>The most instruments in one subscribe message (Dhan's limit).</summary>
    public const int SubscribeBatch = ILiveFeedConnection.MaxBatch;

    public static readonly TimeSpan BackoffInitial = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan BackoffMax = TimeSpan.FromSeconds(30);

    public const int NotSubscribedCode = 806;

    public const int TooManyConnectionsCode = 805;

    private readonly Lock _gate = new();
    private readonly List<TradeTick> _pending = [];
    private volatile string _state = DepthStates.Closed;
    private long _packets;
    private long _rows;
    private long _dropped;
    private long _gapTicks;
    private int _reconnects;
    private TickSampler? _sampler;
    private DepthSessionEnd? _guardEnd;
    private CancellationTokenSource? _inner;

    /// <summary>For the tests only: a smaller queue cap, so that the cap is reached without pushing 50,000 packets.</summary>
    internal TickRecorder(IServiceScopeFactory scopes, ITickFeedConnector connector, TickSettings settings, TimeProvider clock, ILogger<TickRecorder> logger, int maxPending)
        : this(scopes, connector, settings, clock, logger) => _maxPending = maxPending;

    public string State => _state;

    public TickRunStats Stats => new(
        Interlocked.Read(ref _packets),
        Interlocked.Read(ref _rows),
        _sampler?.Unchanged ?? 0,
        Interlocked.Read(ref _dropped),
        (int)TimeSpan.FromTicks(Interlocked.Read(ref _gapTicks)).TotalSeconds,
        Volatile.Read(ref _reconnects));

    public async Task<TickSessionResult> RunAsync(IReadOnlyList<TickSetEntry> set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        Reset();
        var started = clock.GetTimestamp();
        var sampler = new TickSampler(set.ToDictionary(e => e.SecurityId, e => e.InstrumentId));
        _sampler = sampler;
        using var inner = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _inner = inner;
        var maintain = MaintainAsync(inner.Token);
        string? fingerprint = null;
        var end = DepthSessionEnd.Stopped;
        try
        {
            (end, fingerprint) = await ConnectLoopAsync(set, sampler, started, inner.Token);
        }
        catch (OperationCanceledException) when (inner.IsCancellationRequested)
        {
            end = _guardEnd ?? DepthSessionEnd.Stopped;
        }
        finally
        {
            await inner.CancelAsync();
            await maintain;
            await FinalFlushAsync();
            _state = DepthStates.Closed;
        }

        return new TickSessionResult(_guardEnd ?? end, fingerprint, Stats);
    }

    private void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
        }

        _packets = _rows = _dropped = _gapTicks = 0;
        _reconnects = 0;
        _guardEnd = null;
        _sampler = null;
        _state = DepthStates.Connecting;
    }

    private async Task<(DepthSessionEnd End, string? Fingerprint)> ConnectLoopAsync(IReadOnlyList<TickSetEntry> set, TickSampler sampler, long started, CancellationToken cancellationToken)
    {
        var limiter = new ReconnectLimiter();
        var backoff = new ReconnectBackoff();
        var first = true;
        var downSince = clock.GetUtcNow();
        string? fingerprint = null;
        long sequence = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!first)
            {
                Interlocked.Increment(ref _reconnects);
                if (limiter.Record(clock.GetUtcNow()))
                {
                    _state = DepthStates.Flapping;
                    TickLog.Flapping(logger, limiter.Count, (int)ReconnectLimiter.Window.TotalMinutes, (int)ReconnectLimiter.Pause.TotalMinutes);
                    return (DepthSessionEnd.Flapping, fingerprint);
                }

                _state = DepthStates.Reconnecting;
                await Task.Delay(backoff.Next(BackoffInitial, BackoffMax), clock, cancellationToken);
            }

            first = false;
            DataAccountCredentials? credentials;
            await using (var scope = scopes.CreateAsyncScope())
            {
                credentials = await scope.ServiceProvider.GetRequiredService<IDataAccountCredentials>().GetCredentialsAsync(cancellationToken);
            }

            if (credentials is null)
            {
                return (DepthSessionEnd.NoToken, fingerprint);
            }

            fingerprint = FeedCredentialFingerprint.Of(credentials);
            var connected = await TryConnectAsync(credentials, cancellationToken);
            if (connected.Failure == FeedConnectFailureKind.TokenRefused)
            {
                _state = DepthStates.TokenRefused;
                TickLog.TokenRefused(logger);
                return (DepthSessionEnd.TokenRefused, fingerprint);
            }

            if (connected.Connection is not { } connection)
            {
                TickLog.ConnectFailed(logger, connected.Message ?? "the connection could not be made");
                continue;
            }

            await using (connection)
            {
                Interlocked.Add(ref _gapTicks, Math.Max(0, (clock.GetUtcNow() - downSince).Ticks));
                sampler.Rebase(); // the volume traded while away shows as one jump, never as one instant's trade
                var sequenceBefore = sequence;
                (var outcome, sequence) = await StreamAsync(connection, set, sampler, started, sequence, cancellationToken);
                if (sequence != sequenceBefore)
                {
                    backoff.Reset(); // a connection that delivered data starts the back-off again from its first step
                }

                downSince = clock.GetUtcNow();
                switch (outcome)
                {
                    case DepthSessionEnd.TokenRefused:
                        _state = DepthStates.TokenRefused;
                        TickLog.TokenRefused(logger);
                        return (outcome, fingerprint);
                    case DepthSessionEnd.NotSubscribed:
                        _state = DepthStates.NotSubscribed;
                        TickLog.TokenRefused(logger);
                        return (outcome, fingerprint);
                    case DepthSessionEnd.Flapping:
                        _state = DepthStates.Flapping;
                        return (outcome, fingerprint);
                    default:
                        break; // an ordinary drop: reconnect
                }
            }
        }
    }

    /// <summary>A connector that throws (it should not: failures are results) is a failed attempt; only the type is logged, because the text could name the endpoint.</summary>
    private async ValueTask<FeedConnectResult> TryConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        try
        {
            return await connector.ConnectAsync(credentials, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // see the summary
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.ConnectionError(logger, ex.GetType().Name);
            return FeedConnectResult.Failed(FeedConnectFailureKind.Failed, "the connection could not be made");
        }
    }

    /// <summary>One connection from its subscription to its end. Returns <see cref="DepthSessionEnd.Stopped"/> for an ordinary drop (the caller reconnects) or the end a disconnect code asks for, and the packet counter so far.</summary>
    private async Task<(DepthSessionEnd Outcome, long Sequence)> StreamAsync(ILiveFeedConnection connection, IReadOnlyList<TickSetEntry> set, TickSampler sampler, long started, long sequence, CancellationToken cancellationToken)
    {
        TickLog.Connected(logger);
        var outcome = DepthSessionEnd.Stopped;
        var reason = "stopped";
        try
        {
            foreach (var batch in set.Select(e => new FeedSecurity(FeedSegments.NseEquity, e.SecurityId)).Chunk(SubscribeBatch))
            {
                await connection.SubscribeAsync(FeedMode.Quote, batch, cancellationToken);
            }

            TickLog.Subscribed(logger, set.Count);
            _state = DepthStates.Recording;
            await foreach (var packet in connection.ReadAsync(cancellationToken))
            {
                if (packet.Kind == FeedPacketKind.Disconnect)
                {
                    outcome = packet.Reason switch
                    {
                        NotSubscribedCode => DepthSessionEnd.NotSubscribed,
                        TooManyConnectionsCode => DepthSessionEnd.Flapping,
                        var code when FeedDisconnectReasons.IsTokenProblem(code) => DepthSessionEnd.TokenRefused,
                        _ => DepthSessionEnd.Stopped,
                    };
                    reason = "server code " + packet.Reason.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                }

                Interlocked.Increment(ref _packets);
                sequence++;
                Enqueue(sampler.Offer(packet, clock.GetUtcNow(), (long)clock.GetElapsedTime(started).TotalMilliseconds, sequence));
            }

            if (outcome == DepthSessionEnd.Stopped)
            {
                reason = connection.EndReason.Length > 0 ? connection.EndReason : "ended";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed connection is an ordinary drop; only its type is logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.ConnectionError(logger, ex.GetType().Name);
            reason = "error";
        }

        TickLog.Disconnected(logger, reason);
        return (outcome, sequence);
    }

    private void Enqueue(TradeTick? row)
    {
        if (row is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_pending.Count >= _maxPending)
            {
                // a database that hangs must not grow memory: the new row is dropped and counted (the 30-second rule drops the oldest once writing works again)
                Interlocked.Increment(ref _dropped);
                return;
            }

            _pending.Add(row);
        }

        Interlocked.Increment(ref _rows);
    }

    /// <summary>The flush every <c>Ticks:FlushSeconds</c> and the disk guard every five minutes, until the run ends. A failing flush or guard read is logged and tried again; it never ends the run.</summary>
    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        var nextGuard = clock.GetUtcNow() + DepthDiskGuard.Interval;
        try
        {
            using var timer = new PeriodicTimer(settings.Flush, clock);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await FlushAsync(cancellationToken);
                var now = clock.GetUtcNow();
                if (now >= nextGuard)
                {
                    nextGuard = now + DepthDiskGuard.Interval;
                    await GuardAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the run ended
        }
    }

    /// <summary>Writes the queue in one transaction. A failed batch is kept for the next flush; rows older than <see cref="MaxUnwritten"/> are dropped (oldest first) and counted.</summary>
    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        TradeTick[] batch;
        lock (_gate)
        {
            var dropped = _pending.RemoveAll(r => clock.GetUtcNow() - r.Ts > MaxUnwritten);
            if (dropped > 0)
            {
                TickLog.Dropped(logger, dropped, Interlocked.Add(ref _dropped, dropped));
            }

            batch = [.. _pending];
        }

        if (batch.Length == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITickStore>().AppendAsync(batch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the batch stays queued; the next flush tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.FlushFailed(logger, ex.GetType().Name);
            return;
        }

        lock (_gate)
        {
            _pending.RemoveRange(0, Math.Min(batch.Length, _pending.Count)); // rows are only ever added at the end; the drop above removes from the front only what was never part of a written batch
        }

        TickLog.Flush(logger, batch.Length);
    }

    private async Task GuardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ITickStore>();
            var stored = await store.StoredBytesAsync(cancellationToken);
            var free = await store.FreeBytesAsync(cancellationToken);
            if (free is null)
            {
                TickLog.FreeSpaceUnreadable(logger);
            }

            switch (DepthDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes))
            {
                case DepthDiskVerdict.Warning:
                    TickLog.DiskWarning(logger, stored / (1024 * 1024), settings.MaxStoredBytes / (1024 * 1024));
                    break;
                case DepthDiskVerdict.OverBudget:
                    Stop(DepthSessionEnd.DiskBudget, "disk_budget", DepthStates.DiskBudget);
                    break;
                case DepthDiskVerdict.LowFree:
                    Stop(DepthSessionEnd.DiskFree, "disk_free", DepthStates.DiskFree);
                    break;
                default:
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a guard that cannot read is tried again in five minutes; the other measure stays in force
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.ConnectionError(logger, ex.GetType().Name);
        }
    }

    private void Stop(DepthSessionEnd end, string reason, string state)
    {
        _guardEnd = end;
        _state = state;
        TickLog.DiskStop(logger, reason);
        _inner?.Cancel();
    }

    /// <summary>The last write after the run ended, bounded, whatever happened to the token of the run.</summary>
    private async Task FinalFlushAsync()
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        try
        {
            await FlushAsync(wait.Token);
        }
        catch (OperationCanceledException)
        {
            // the rows that did not make it are lost; the next run starts clean
        }
    }
}
