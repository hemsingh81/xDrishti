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

    /// <summary>The median exchange-to-receive skew above which the exchange time cannot be trusted.</summary>
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    private const int SkewSamples = 200;

    private const int SkewMinSamples = 10;

    private const int NotSubscribedCode = 806;

    private const int TooManyConnectionsCode = 805;

    private readonly Lock _gate = new();
    private readonly List<MinuteBar> _pending = [];
    private readonly List<(DateOnly Day, OpeningBarsEvent Event)> _events = [];
    private MinuteBarBuilder _builder = new(OpeningBarsSettings.Open, OpeningBarsSettings.Off.RecordUntil);
    private DateOnly _day;
    private DateTimeOffset _lastPacket;
    private DateTimeOffset? _gapFrom;
    private readonly List<(DateTimeOffset From, DateTimeOffset? To)> _gapRanges = [];
    private readonly List<double> _skews = [];
    private bool _skewTripped;
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
        _gapFrom = null;
        _gapRanges.Clear();
        _skews.Clear();
        _skewTripped = false;
        _guardEnd = null;
    }

    private OpeningRunStats Stats(DateTimeOffset until)
    {
        var expected = (long)_instruments * Math.Max(0, (int)(Min(until, clock.GetUtcNow()) - OpeningWindows.At(_day, OpeningBarsSettings.Open)).TotalMinutes - 1);
        lock (_gate)
        {
            return new OpeningRunStats(_instruments, Interlocked.Read(ref _packets), Interlocked.Read(ref _bars), Math.Max(0, expected - Interlocked.Read(ref _bars)), _builder.Late, Interlocked.Read(ref _duplicates), _gaps, _builder.Outside, MedianSkewSeconds());
        }
    }

    /// <summary>The median of (receive time minus exchange time) of the recent packets, in seconds (0 with none). Caller holds the gate.</summary>
    private int MedianSkewSeconds() => _skews.Count == 0 ? 0 : (int)_skews.Order().ElementAt(_skews.Count / 2);

    /// <summary>A gap starts: written as an <b>open</b> event at once (it is on record while it lasts), closed by <see cref="EndGap"/>. Only one is open at a time. Caller holds the gate.</summary>
    private void BeginGap(DateTimeOffset from)
    {
        if (_gapFrom is not null)
        {
            return;
        }

        _gapFrom = from;
        _gapRanges.Add((from, null));
        _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.Gap, from)));
        _gaps++;
    }

    /// <summary>The open gap, if any, ends now (a second event with the end; the open one stays: a reader takes the closed one). Caller holds the gate.</summary>
    private void EndGap(DateTimeOffset now)
    {
        if (_gapFrom is not { } from)
        {
            return;
        }

        _gapRanges[^1] = (from, now);
        _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.Gap, from, now)));
        _gapFrom = null;
    }

    /// <summary>Queues the completed bars except those of a minute a gap overlaps: such a minute is incomplete and is never written as complete. Caller holds the gate.</summary>
    private void Queue(IEnumerable<MinuteBar> bars)
    {
        foreach (var bar in bars)
        {
            var end = bar.MinuteStart.AddMinutes(1);
            if (!_gapRanges.Any(g => g.From < end && (g.To ?? DateTimeOffset.MaxValue) > bar.MinuteStart))
            {
                _pending.Add(bar);
            }
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
                MarkDown();
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
            MarkDown(); // an ordinary drop: a gap starts (on record at once) and the loop reconnects
        }
    }

    private void MarkDown()
    {
        lock (_gate)
        {
            BeginGap(clock.GetUtcNow());
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
                EndGap(now); // a drop ends at the new subscription
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
            EndGap(now); // a silence ends at the next packet
            if (_builder.Offer(instrument, time, packet.Price, volume) is { } bar)
            {
                Queue([bar]);
            }

            // Dhan's trade-time zone is UNVERIFIED: when the exchange time is far from the receive time the minutes would be wrong, so the run stops and the day is not formed (clock_skew), never guessed.
            _skews.Add((now - time).TotalSeconds);
            if (_skews.Count > SkewSamples)
            {
                _skews.RemoveAt(0);
            }

            if (!_skewTripped && _skews.Count >= SkewMinSamples && Math.Abs(MedianSkewSeconds()) > MaxSkew.TotalSeconds)
            {
                _skewTripped = true;
                _guardEnd = OpeningSessionEnd.ClockSkew;
                _events.Add((_day, new OpeningBarsEvent(OpeningBarsEvent.ClockSkew, now)));
                _inner?.Cancel();
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
                    if (now >= open && now < until && now - (_lastPacket > open ? _lastPacket : open) > settings.Silence)
                    {
                        BeginGap(_lastPacket > open ? _lastPacket : open); // on record at once, so a freeze during the silence sees it
                    }

                    Queue(_builder.Flush(now, settings.Grace));
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
            return free is not { } bytes || bytes < settings.MinFreeBytes; // a free space that cannot be read is a low one, never a pass
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
            Queue(_builder.Flush(clock.GetUtcNow(), TimeSpan.Zero)); // a gap still open stays open on record (written when it began)
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
