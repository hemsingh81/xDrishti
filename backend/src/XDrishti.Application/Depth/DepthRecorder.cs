using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Live;

namespace XDrishti.Application.Depth;

/// <summary>
/// The connection of the depth recorder for one run (P7-16 criteria 2 to 4, 7, 8): connect with the primary Data account's credentials, subscribe to the day's set in ONE message, then stream. The receive loop hands each packet to the
/// <see cref="DepthSampler"/> and queues the snapshots that fall in 09:15:00 to 15:30:00; it never waits for the database. A maintenance loop beside it writes the queue every <c>Depth:FlushSeconds</c> (one transaction; a failed batch stays for the
/// next flush; rows unwritten for more than 30 seconds are dropped oldest first and counted) and asks the <see cref="DepthDiskGuard"/> every five minutes. A dropped connection is replaced after the back-off with the same set; more than 10 attempts in
/// 5 minutes end the run as flapping, a refused token or a 806 end it for good until the credentials change. Dhan's disconnect codes: 806 not subscribed, 805 too many connections (flapping), 802/803/807/808/809 token. The live feed's session is
/// untouched: another connection, other state, its own catch.
/// </summary>
internal sealed class DepthRecorder(IServiceScopeFactory scopes, IDepthFeedConnector connector, DepthSettings settings, TimeProvider clock, ILogger<DepthRecorder> logger) : IDepthSession
{
    /// <summary>Rows unwritten for longer than this are dropped, oldest first.</summary>
    public static readonly TimeSpan MaxUnwritten = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan BackoffInitial = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan BackoffMax = TimeSpan.FromSeconds(30);

    public const int NotSubscribedCode = 806;

    public const int TooManyConnectionsCode = 805;

    private readonly Lock _gate = new();
    private readonly List<DepthSnapshot> _pending = [];
    private volatile string _state = DepthStates.Closed;
    private long _snapshots;
    private long _packets;
    private long _dropped;
    private long _gapTicks;
    private int _reconnects;
    private DepthSessionEnd? _guardEnd;
    private CancellationTokenSource? _inner;

    public string State => _state;

    public DepthRunStats Stats => new(Interlocked.Read(ref _snapshots), Interlocked.Read(ref _packets), Interlocked.Read(ref _dropped), (int)TimeSpan.FromTicks(Interlocked.Read(ref _gapTicks)).TotalSeconds, Volatile.Read(ref _reconnects));

    public async Task<DepthSessionResult> RunAsync(IReadOnlyList<DepthSetEntry> set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        Reset();
        var started = clock.GetTimestamp();
        var sampler = new DepthSampler(set.ToDictionary(e => e.SecurityId, e => e.InstrumentId), settings.Sample);
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

        return new DepthSessionResult(_guardEnd ?? end, fingerprint, Stats);
    }

    private void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
        }

        _snapshots = _packets = _dropped = _gapTicks = 0;
        _reconnects = 0;
        _guardEnd = null;
        _state = DepthStates.Connecting;
    }

    private async Task<(DepthSessionEnd End, string? Fingerprint)> ConnectLoopAsync(IReadOnlyList<DepthSetEntry> set, DepthSampler sampler, long started, CancellationToken cancellationToken)
    {
        var limiter = new ReconnectLimiter();
        var backoff = new ReconnectBackoff();
        var first = true;
        var downSince = clock.GetUtcNow();
        string? fingerprint = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!first)
            {
                var now = clock.GetUtcNow();
                Interlocked.Increment(ref _reconnects);
                if (limiter.Record(now))
                {
                    _state = DepthStates.Flapping;
                    DepthLog.Flapping(logger, limiter.Count, (int)ReconnectLimiter.Window.TotalMinutes, (int)ReconnectLimiter.Pause.TotalMinutes);
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
                DepthLog.TokenRefused(logger);
                return (DepthSessionEnd.TokenRefused, fingerprint);
            }

            if (connected.Connection is not { } connection)
            {
                DepthLog.ConnectFailed(logger, connected.Message ?? "the connection could not be made");
                continue;
            }

            await using (connection)
            {
                var gapEnded = clock.GetUtcNow();
                Interlocked.Add(ref _gapTicks, Math.Max(0, (gapEnded - downSince).Ticks));
                var outcome = await StreamAsync(connection, set, sampler, started, cancellationToken);
                downSince = clock.GetUtcNow();
                switch (outcome)
                {
                    case DepthSessionEnd.TokenRefused:
                        _state = DepthStates.TokenRefused;
                        DepthLog.TokenRefused(logger);
                        return (outcome, fingerprint);
                    case DepthSessionEnd.NotSubscribed:
                        _state = DepthStates.NotSubscribed;
                        DepthLog.TokenRefused(logger);
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
    private async ValueTask<DepthConnectResult> TryConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
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
            DepthLog.ConnectionError(logger, ex.GetType().Name);
            return DepthConnectResult.Failed(FeedConnectFailureKind.Failed, "the connection could not be made");
        }
    }

    /// <summary>One connection from its subscription to its end. Returns <see cref="DepthSessionEnd.Stopped"/> for an ordinary drop (the caller reconnects), or the end the disconnect code asks for.</summary>
    private async Task<DepthSessionEnd> StreamAsync(IDepthFeedConnection connection, IReadOnlyList<DepthSetEntry> set, DepthSampler sampler, long started, CancellationToken cancellationToken)
    {
        DepthLog.Connected(logger);
        var outcome = DepthSessionEnd.Stopped;
        var reason = "stopped";
        try
        {
            await connection.SubscribeAsync([.. set.Select(e => e.SecurityId)], cancellationToken);
            DepthLog.Subscribed(logger, set.Count);
            _state = DepthStates.Recording;
            var sweepMs = settings.Sample.TotalMilliseconds;
            long lastSweep = 0;
            await foreach (var packet in connection.ReadAsync(cancellationToken))
            {
                if (packet.Kind == DepthPacketKind.Disconnect)
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
                var now = clock.GetUtcNow();
                var mono = (long)clock.GetElapsedTime(started).TotalMilliseconds;
                Enqueue(sampler.Offer(packet, now, mono));
                if (mono - lastSweep >= sweepMs)
                {
                    lastSweep = mono;
                    foreach (var due in sampler.Due(now, mono))
                    {
                        Enqueue(due);
                    }
                }
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
            DepthLog.ConnectionError(logger, ex.GetType().Name);
            reason = "error";
        }

        DepthLog.Disconnected(logger, reason);
        return outcome;
    }

    private void Enqueue(DepthSnapshot? snapshot)
    {
        if (snapshot is null || !DepthWindow.Contains(snapshot.Ts))
        {
            return;
        }

        lock (_gate)
        {
            _pending.Add(snapshot);
        }

        Interlocked.Increment(ref _snapshots);
    }

    /// <summary>The flush every <c>Depth:FlushSeconds</c> and the disk guard every five minutes, until the run ends. A failing flush or guard read is logged and tried again; it never ends the run.</summary>
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
        DepthSnapshot[] batch;
        lock (_gate)
        {
            var dropped = _pending.RemoveAll(r => clock.GetUtcNow() - r.Ts > MaxUnwritten);
            if (dropped > 0)
            {
                DepthLog.Dropped(logger, dropped, Interlocked.Add(ref _dropped, dropped));
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
            await scope.ServiceProvider.GetRequiredService<IDepthStore>().AppendAsync(batch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the batch stays queued; the next flush tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DepthLog.FlushFailed(logger, ex.GetType().Name);
            return;
        }

        lock (_gate)
        {
            _pending.RemoveRange(0, Math.Min(batch.Length, _pending.Count)); // rows are only ever added at the end, and the drop above removes from the front only what was never part of a written batch
        }

        DepthLog.Flush(logger, batch.Length);
    }

    private async Task GuardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDepthStore>();
            var stored = await store.StoredBytesAsync(cancellationToken);
            var free = await store.FreeBytesAsync(cancellationToken);
            if (free is null)
            {
                DepthLog.FreeSpaceUnreadable(logger);
            }

            switch (DepthDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes))
            {
                case DepthDiskVerdict.Warning:
                    DepthLog.DiskWarning(logger, stored / (1024 * 1024), settings.MaxStoredBytes / (1024 * 1024));
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
            DepthLog.ConnectionError(logger, ex.GetType().Name);
        }
    }

    private void Stop(DepthSessionEnd end, string reason, string state)
    {
        _guardEnd = end;
        _state = state;
        DepthLog.DiskStop(logger, reason);
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
