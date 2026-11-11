using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Live;

namespace XDrishti.Application.OpeningBars;

/// <summary>
/// One recording session of the opening minutes (BK-03b), on <b>its own connection</b> (a second socket to the live feed's allowed host, never the live feed's - the live feed's subscription list and cap are untouched). Connect with the Data account's
/// credentials, subscribe the day's set in quote mode (batches of 100), build 1-minute bars from the packets' exchange time (<see cref="MinuteBarBuilder"/>), write the completed bars every second in one statement (insert-only; a bar that exists is a
/// duplicate, counted), and tell the derived job what it needs to know: <see cref="OpeningBarsEvent.Started"/> when subscribed, a <see cref="OpeningBarsEvent.Gap"/> for every drop or connection-wide silence above <c>OpeningBars:Silence</c>, and
/// <see cref="OpeningBarsEvent.LowDisk"/> when it stopped for the disk. It stops at <c>RecordUntil</c>: unsubscribes and closes. An event that cannot be written is kept and tried again every second; a bar that cannot be written stays queued.
/// </summary>
internal sealed class OpeningBarsRecorder(IServiceScopeFactory scopes, IOpeningBarsFeedConnector connector, OpeningBarsSettings settings, TimeProvider clock, ILogger<OpeningBarsRecorder> logger)
{
    public static readonly TimeSpan BackoffInitial = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan BackoffMax = TimeSpan.FromSeconds(10);

    /// <summary>How often the free disk space is looked at while recording.</summary>
    public static readonly TimeSpan DiskInterval = TimeSpan.FromSeconds(30);

    private const int NotSubscribedCode = 806;

    private const int TooManyConnectionsCode = 805;

    private readonly Lock _gate = new();
    private readonly List<MinuteBar> _pending = [];
    private readonly List<(DateOnly Day, OpeningBarsEvent Event)> _events = [];
    private MinuteBarBuilder _builder = new(OpeningBarsSettings.Open, OpeningBarsSettings.Off.RecordUntil);
    private DateOnly _day;
    private DateTimeOffset _lastPacket;
    private DateTimeOffset? _silentFrom;
    private DateTimeOffset? _downSince;
    private long _packets;
    private long _bars;
    private long _duplicates;
    private int _gaps;
    private int _instruments;
    private OpeningSessionEnd? _guardEnd;
    private CancellationTokenSource? _inner;

    /// <summary>The packets of the set read so far (for the tests, which wait for them).</summary>
    internal long PacketsRead => Interlocked.Read(ref _packets);

    /// <summary>Runs until <c>RecordUntil</c>, a closed gate (cancellation), the disk or a refusal. Cancellation is reported as <see cref="OpeningSessionEnd.Stopped"/>, not thrown.</summary>
    public async Task<OpeningSessionResult> RunAsync(DateOnly day, IReadOnlyList<OpeningSetEntry> set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        Reset(day, set.Count);
        var started = clock.GetTimestamp();
        var until = OpeningWindows.At(day, settings.RecordUntil);
        using var inner = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _inner = inner;
        var maintain = MaintainAsync(until, inner.Token);
        var end = OpeningSessionEnd.Stopped;
        try
        {
            end = await ConnectLoopAsync(set, until, inner.Token);
        }
        catch (OperationCanceledException) when (inner.IsCancellationRequested)
        {
            end = _guardEnd ?? OpeningSessionEnd.Stopped;
        }
        finally
        {
            await inner.CancelAsync();
            await maintain;
            await FinishAsync();
        }

        var result = new OpeningSessionResult(_guardEnd ?? end, Stats(until));
        var stats = result.Stats;
        var milliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        var word = result.End.ToString();
        OpeningBarsLog.RunEnded(logger, word, stats.Instruments, stats.Bars, stats.Missing, stats.Late, stats.Duplicates, stats.Gaps, milliseconds);
        return result;
    }

    private void Reset(DateOnly day, int instruments)
    {
        lock (_gate)
        {
            _pending.Clear();
            _events.Clear();
            _builder = new MinuteBarBuilder(OpeningBarsSettings.Open, settings.RecordUntil);
        }

        _day = day;
        _instruments = instruments;
        _packets = _bars = _duplicates = 0;
        _gaps = 0;
        _lastPacket = default;
        _silentFrom = null;
        _downSince = null;
        _guardEnd = null;
    }

    private OpeningRunStats Stats(DateTimeOffset until)
    {
        var expected = (long)_instruments * Math.Max(0, (int)(Min(until, clock.GetUtcNow()) - OpeningWindows.At(_day, OpeningBarsSettings.Open)).TotalMinutes - 1);
        lock (_gate)
        {
            return new OpeningRunStats(_instruments, Interlocked.Read(ref _packets), Interlocked.Read(ref _bars), Math.Max(0, expected - Interlocked.Read(ref _bars)), _builder.Late, Interlocked.Read(ref _duplicates), _gaps);
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private async Task<OpeningSessionEnd> ConnectLoopAsync(IReadOnlyList<OpeningSetEntry> set, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var limiter = new ReconnectLimiter();
        var backoff = new ReconnectBackoff();
        var byId = set.ToDictionary(e => e.SecurityId, e => e.InstrumentId);
        var first = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.GetUtcNow() >= until)
            {
                return OpeningSessionEnd.Until;
            }

            if (!first)
            {
                if (limiter.Record(clock.GetUtcNow()))
                {
                    return OpeningSessionEnd.Refused;
                }

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
                return OpeningSessionEnd.NoToken;
            }

            var connected = await TryConnectAsync(credentials, cancellationToken);
            if (connected.Failure == FeedConnectFailureKind.TokenRefused)
            {
                return OpeningSessionEnd.TokenRefused;
            }

            if (connected.Connection is not { } connection)
            {
                _downSince ??= clock.GetUtcNow();
                continue;
            }

            await using (connection)
            {
                var outcome = await StreamAsync(connection, set, byId, cancellationToken);
                if (outcome is { } ended)
                {
                    return ended;
                }
            }

            cancellationToken.ThrowIfCancellationRequested(); // a stream that ends because the run was stopped is not a drop
            _downSince ??= clock.GetUtcNow(); // an ordinary drop: a gap starts and the loop reconnects
        }
    }

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
#pragma warning disable CA1031 // a connector that throws (it should not) is a failed attempt; only the type is logged, the text could name the endpoint
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OpeningBarsLog.Problem(logger, ex.GetType().Name);
            return FeedConnectResult.Failed(FeedConnectFailureKind.Failed, "the connection could not be made");
        }
    }

    /// <summary>One connection from its subscription to its end; null for an ordinary drop (the caller reconnects), else the end a disconnect code asks for.</summary>
    private async Task<OpeningSessionEnd?> StreamAsync(ILiveFeedConnection connection, IReadOnlyList<OpeningSetEntry> set, Dictionary<int, Guid> byId, CancellationToken cancellationToken)
    {
        OpeningSessionEnd? outcome = null;
        try
        {
            foreach (var batch in set.Select(e => new FeedSecurity(FeedSegments.NseEquity, e.SecurityId)).Chunk(ILiveFeedConnection.MaxBatch))
            {
                await connection.SubscribeAsync(FeedMode.Quote, batch, cancellationToken);
            }

            OpeningBarsLog.Subscribed(logger, set.Count);
            var now = clock.GetUtcNow();
            lock (_gate)
            {
                _lastPacket = now;
                if (_downSince is { } down)
                {
                    _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.Gap, down, now)));
                    _gaps++;
                    _downSince = null;
                }

                _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.Started, now))); // a second start in a day is itself a hole the derived job reads as a gap
            }

            await foreach (var packet in connection.ReadAsync(cancellationToken))
            {
                if (packet.Kind == FeedPacketKind.Disconnect)
                {
                    outcome = packet.Reason switch
                    {
                        NotSubscribedCode or TooManyConnectionsCode => OpeningSessionEnd.Refused,
                        var code when FeedDisconnectReasons.IsTokenProblem(code) => OpeningSessionEnd.TokenRefused,
                        _ => null,
                    };
                    break;
                }

                Offer(packet, byId);
            }

            await UnsubscribeAsync(connection, set);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await UnsubscribeAsync(connection, set);
            throw;
        }
#pragma warning disable CA1031 // a failed connection is an ordinary drop; only its type is logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OpeningBarsLog.Problem(logger, ex.GetType().Name);
        }

        return outcome;
    }

    private void Offer(FeedPacket packet, Dictionary<int, Guid> byId)
    {
        if (packet.Kind != FeedPacketKind.Quote || packet.Security.Segment != FeedSegments.NseEquity || !byId.TryGetValue(packet.Security.SecurityId, out var instrument)
            || packet.Volume is not { } volume || packet.TradeTime is not { } time)
        {
            return;
        }

        var now = clock.GetUtcNow();
        Interlocked.Increment(ref _packets);
        lock (_gate)
        {
            _lastPacket = now;
            if (_silentFrom is { } silent)
            {
                _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.Gap, silent, now)));
                _gaps++;
                _silentFrom = null;
            }

            if (_builder.Offer(instrument, time, packet.Price, volume) is { } bar)
            {
                _pending.Add(bar);
            }
        }
    }

    /// <summary>Best effort and bounded: the recorder closes by unsubscribing the day's set, whatever ended the run.</summary>
    private async Task UnsubscribeAsync(ILiveFeedConnection connection, IReadOnlyList<OpeningSetEntry> set)
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        try
        {
            foreach (var batch in set.Select(e => new FeedSecurity(FeedSegments.NseEquity, e.SecurityId)).Chunk(ILiveFeedConnection.MaxBatch))
            {
                await connection.UnsubscribeAsync(FeedMode.Quote, batch, wait.Token);
            }
        }
#pragma warning disable CA1031 // closing is best effort: the socket is disposed next
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OpeningBarsLog.Problem(logger, ex.GetType().Name);
        }
    }

    /// <summary>Every second: complete the bars of finished minutes, write the queue, write the events, look for a silence, stop at <c>RecordUntil</c>, look at the disk every 30 seconds.</summary>
    private async Task MaintainAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        var nextDisk = clock.GetUtcNow();
        var open = OpeningWindows.At(_day, OpeningBarsSettings.Open);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = clock.GetUtcNow();
                lock (_gate)
                {
                    _pending.AddRange(_builder.Flush(now, settings.Grace));
                    if (_silentFrom is null && _downSince is null && now >= open && now < until && now - (_lastPacket > open ? _lastPacket : open) > settings.Silence)
                    {
                        _silentFrom = _lastPacket > open ? _lastPacket : open;
                    }
                }

                await WriteAsync(cancellationToken);
                if (now >= until)
                {
                    _guardEnd = OpeningSessionEnd.Until;
                    await _inner!.CancelAsync();
                    return;
                }

                if (now >= nextDisk)
                {
                    nextDisk = now + DiskInterval;
                    if (await LowDiskAsync(cancellationToken))
                    {
                        _guardEnd = OpeningSessionEnd.LowDisk;
                        lock (_gate)
                        {
                            _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.LowDisk, now)));
                        }

                        OpeningBarsLog.LowDisk(logger);
                        await WriteAsync(cancellationToken);
                        await _inner!.CancelAsync();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the run ended
        }
    }

    private async Task<bool> LowDiskAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var free = await scope.ServiceProvider.GetRequiredService<IOpeningBarsStore>().FreeBytesAsync(cancellationToken);
            return free is { } bytes && bytes < settings.MinFreeBytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            OpeningBarsLog.FlushFailed(logger, ex.GetType().Name); // a guard that cannot read is tried again in 30 seconds
            return false;
        }
    }

    /// <summary>Writes the queued bars (one statement) and events. A failure keeps them queued for the next second; nothing is dropped.</summary>
    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        MinuteBar[] bars;
        (DateOnly Day, OpeningBarsEvent Event)[] events;
        lock (_gate)
        {
            bars = [.. _pending];
            events = [.. _events];
        }

        if (bars.Length == 0 && events.Length == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOpeningBarsStore>();
            foreach (var (day, @event) in events)
            {
                await store.AddEventAsync(day, @event, cancellationToken);
            }

            lock (_gate)
            {
                _events.RemoveRange(0, Math.Min(events.Length, _events.Count)); // the events are written; the next try must not repeat them
            }

            if (bars.Length > 0)
            {
                var added = await store.AppendAsync(bars, cancellationToken);
                Interlocked.Add(ref _bars, added);
                Interlocked.Add(ref _duplicates, bars.Length - added);
                lock (_gate)
                {
                    _pending.RemoveRange(0, Math.Min(bars.Length, _pending.Count));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // what did not go in stays queued; the next second tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OpeningBarsLog.FlushFailed(logger, ex.GetType().Name);
        }
    }

    /// <summary>The last write after the run ended, bounded: the minutes that are over are completed, an open silence or drop is recorded as an open gap, the queue goes out.</summary>
    private async Task FinishAsync()
    {
        lock (_gate)
        {
            _pending.AddRange(_builder.Flush(clock.GetUtcNow(), TimeSpan.Zero));
            if ((_silentFrom ?? _downSince) is { } since)
            {
                _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.Gap, since)));
                _gaps++;
                _silentFrom = _downSince = null;
            }
        }

        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        try
        {
            await WriteAsync(wait.Token);
        }
        catch (OperationCanceledException)
        {
            // what did not make it is lost; the derived job then sees a missing minute and says so
        }
    }
}
