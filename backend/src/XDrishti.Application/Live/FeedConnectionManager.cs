using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Live;

/// <summary>
/// The connection of the live feed for one window (ADR 0024, P2-10 criteria 4 to 8, 17). <b>One connection at a time</b>: connect with the primary Data account's credentials, read the active set and subscribe to all of it (in messages of at
/// most 100, spaced), then stream. Three things run beside each other while connected: the <b>reader</b> maps packets to prices and hands them to <see cref="ILiveFlush"/> (no I/O, no log line per tick; a tick of an instrument outside the set
/// is dropped and counted); the <b>maintenance loop</b> flushes every <c>Live:FlushSeconds</c>, recomputes the set once a minute or when <c>NOTIFY active_set_changed</c> arrived and applies only the difference (subscribe/unsubscribe, no reconnect),
/// and declares the connection dead after <c>Live:SilenceSeconds</c> without a packet in market hours; the <b>listener</b> sets the change flag. A dropped or dead connection is replaced after the back-off (1, 2, 4 ... 30 s, jittered; the
/// whole set is subscribed again from the database). More than 10 reconnect attempts in 5 minutes end the session as <see cref="FeedSessionEnd.Flapping"/> (the supervisor records the failure and pauses); a refused token ends it as
/// <see cref="FeedSessionEnd.TokenRefused"/> (no further attempts until the token changes). Cancellation stops it cleanly with a last flush. A silence is only held against the connection when an index is subscribed: an index ticks all
/// through market hours, a few illiquid shares do not - without one the socket's own ping/pong decides.
/// </summary>
internal sealed class FeedConnectionManager(
    IServiceScopeFactory scopes,
    ILiveFeedConnector connector,
    ActiveSetLoader sets,
    IActiveSetChanges changes,
    ILiveFlush flush,
    FeedMetrics metrics,
    ReconnectBackoff backoff,
    LiveFeedSettings settings,
    TimeProvider clock,
    ILogger<FeedConnectionManager> logger) : IFeedSession
{
    /// <summary>How often the set is read again when no notification arrives (the safety net of <c>NOTIFY active_set_changed</c>).</summary>
    public static readonly TimeSpan RecomputeInterval = TimeSpan.FromMinutes(1);

    /// <summary>How often the summary of the dropped messages is logged.</summary>
    public static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan FinalFlushWait = TimeSpan.FromSeconds(5);

    private volatile string _state = FeedStates.Closed;
    private int _changed;
    private long _loggedDropped;
    private DateTimeOffset _nextSummary;

    public string State => _state;

    public async Task<FeedSessionResult> RunAsync(CancellationToken cancellationToken)
    {
        flush.Reset();
        metrics.Packets.Reset();
        _nextSummary = clock.GetUtcNow() + SummaryInterval;
        backoff.Reset();
        Interlocked.Exchange(ref _changed, 0);
        using var stopWatching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watching = Task.Run(() => WatchAsync(stopWatching.Token), CancellationToken.None);
        try
        {
            return await ConnectLoopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new FeedSessionResult(FeedSessionEnd.Stopped);
        }
        finally
        {
            await stopWatching.CancelAsync();
            await watching;
            metrics.ConnectionEnded();
            _state = FeedStates.Closed;
        }
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await changes.WatchAsync(() => Interlocked.Exchange(ref _changed, 1), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the session ended
        }
#pragma warning disable CA1031 // a lost listener loses nothing: the one-minute recompute is the safety net
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.ActiveSetFailed(logger, ex);
        }
    }

    private async Task<FeedSessionResult> ConnectLoopAsync(CancellationToken cancellationToken)
    {
        var limiter = new ReconnectLimiter();
        var first = true;
        string? fingerprint = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!first)
            {
                var now = clock.GetUtcNow();
                metrics.RecordReconnect(now);
                if (limiter.Record(now))
                {
                    _state = FeedStates.Flapping;
                    LiveFeedLog.Flapping(logger, limiter.Count, (int)ReconnectLimiter.Window.TotalMinutes, (int)ReconnectLimiter.Pause.TotalMinutes);
                    return new FeedSessionResult(FeedSessionEnd.Flapping, fingerprint);
                }

                _state = FeedStates.Reconnecting;
                await Task.Delay(backoff.Next(settings.BackoffInitial, settings.BackoffMax), clock, cancellationToken);
            }
            else
            {
                _state = FeedStates.Connecting;
            }

            first = false;
            var credentials = await ReadCredentialsAsync(cancellationToken);
            if (credentials is null)
            {
                return new FeedSessionResult(FeedSessionEnd.NoToken, fingerprint);
            }

            fingerprint = FeedCredentialFingerprint.Of(credentials);
            var connected = await TryConnectAsync(credentials, cancellationToken);
            if (connected.Failure == FeedConnectFailureKind.TokenRefused)
            {
                _state = FeedStates.TokenRefused;
                LiveFeedLog.TokenRefused(logger);
                return new FeedSessionResult(FeedSessionEnd.TokenRefused, fingerprint);
            }

            if (connected.Connection is not { } connection)
            {
                LiveFeedLog.ConnectFailed(logger, connected.Message ?? "the connection could not be made");
                continue;
            }

            await using (connection)
            {
                if (await RunConnectionAsync(connection, cancellationToken) == ConnectionEnd.TokenRefused)
                {
                    _state = FeedStates.TokenRefused;
                    LiveFeedLog.TokenRefused(logger);
                    return new FeedSessionResult(FeedSessionEnd.TokenRefused, fingerprint);
                }
            }
        }
    }

    /// <summary>A connector that throws (it should not: failures are results) is a failed attempt; only the type of the exception is logged, because its text could name the endpoint.</summary>
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
            LiveFeedLog.ConnectionError(logger, ex.GetType().Name);
            return FeedConnectResult.Failed(FeedConnectFailureKind.Failed, "the connection could not be made");
        }
    }

    private async ValueTask<DataAccountCredentials?> ReadCredentialsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDataAccountCredentials>().GetCredentialsAsync(cancellationToken);
    }

    /// <summary>One connection, from the first subscription to its end. Returns how it ended; a failure of the connection is logged by type and ends it like a drop.</summary>
    private async Task<ConnectionEnd> RunConnectionAsync(ILiveFeedConnection connection, CancellationToken cancellationToken)
    {
        metrics.SetConnected(true);
        LiveFeedLog.Connected(logger);
        var end = ConnectionEnd.Closed;
        var reason = "stopped";
        try
        {
            var link = new Link(clock.GetUtcNow());
            var sync = new SubscriptionSync(connection, settings, clock);
            var set = await sets.LoadAsync(cancellationToken);
            link.Lookup = Lookup(set);
            await sync.ApplyAsync(set, cancellationToken);
            Subscribed(set, sync.Messages);
            link.Touch(clock.GetUtcNow());

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var read = ReadLoopAsync(connection, link, linked.Token);
            var maintain = MaintainLoopAsync(connection, sync, link, linked.Token);
            var finished = await Task.WhenAny(read, maintain);
            await linked.CancelAsync();
            foreach (var task in new[] { read, maintain })
            {
                try
                {
                    var taskEnd = await task;
                    if (task == finished)
                    {
                        end = taskEnd;
                    }
                }
                catch (OperationCanceledException)
                {
                    // the other loop was told to stop (or the caller cancelled: checked below)
                }
#pragma warning disable CA1031 // any failure of the connection ends it like a drop; only the type is logged (the text can name the endpoint)
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LiveFeedLog.ConnectionError(logger, ex.GetType().Name);
                    if (task == finished)
                    {
                        end = ConnectionEnd.Failed;
                    }
                }
            }

            reason = end switch
            {
                ConnectionEnd.Silent => "silence",
                ConnectionEnd.TokenRefused => "token refused",
                ConnectionEnd.Failed => "error",
                _ => connection.EndReason is { Length: > 0 } ended ? ended : "closed",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reason = "stopped";
        }
#pragma warning disable CA1031 // see above
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.ConnectionError(logger, ex.GetType().Name);
            end = ConnectionEnd.Failed;
            reason = "error";
        }

        await FinalFlushAsync();
        metrics.SetConnectionDropped(connection.DroppedPackets);
        metrics.ConnectionEnded();
        LiveFeedLog.Disconnected(logger, reason);
        cancellationToken.ThrowIfCancellationRequested();
        return end;
    }

    private void Subscribed(ActiveSet set, int messages)
    {
        var quote = set.Members.Count(m => m.Mode == FeedMode.Quote);
        metrics.SetSubscribed(set.Members.Count);
        _state = set.Members.Count == 0 ? FeedStates.Idle : FeedStates.Connected;
        LiveFeedLog.Subscribed(logger, set.Members.Count, quote, set.Members.Count - quote, messages);
    }

    private async Task<ConnectionEnd> ReadLoopAsync(ILiveFeedConnection connection, Link link, CancellationToken cancellationToken)
    {
        var healthy = false;
        await foreach (var packet in connection.ReadAsync(cancellationToken))
        {
            var now = clock.GetUtcNow();
            link.Touch(now);
            if (!healthy)
            {
                healthy = true;
                backoff.Reset(); // a connection that delivers data starts the back-off again from one second
            }

            if (packet.Kind == FeedPacketKind.Disconnect)
            {
                return FeedDisconnectReasons.IsTokenProblem(packet.Reason) ? ConnectionEnd.TokenRefused : ConnectionEnd.Closed;
            }

            var mapped = FeedPacketMapper.Map(packet, link.Lookup, now);
            if (mapped.Outcome == PacketOutcome.Price)
            {
                metrics.RecordTick(mapped.Price!.TickAt);
                flush.Add(mapped.Price);
            }
            else if (mapped.Outcome == PacketOutcome.UnknownInstrument)
            {
                metrics.Packets.Reclassify(FeedPacketCodes.Of(packet.Kind), FeedDropReason.UnknownInstrument);
                metrics.RecordDropped(1);
            }
        }

        return ConnectionEnd.Closed;
    }

    private async Task<ConnectionEnd> MaintainLoopAsync(ILiveFeedConnection connection, SubscriptionSync sync, Link link, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(settings.FlushInterval, clock);
        var nextRecompute = clock.GetUtcNow() + RecomputeInterval;
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await FlushAsync(cancellationToken);
            metrics.SetConnectionDropped(connection.DroppedPackets);
            var now = clock.GetUtcNow();
            if (IsSilent(link, sync.Current, now))
            {
                LiveFeedLog.Silent(logger, (int)settings.Silence.TotalSeconds);
                return ConnectionEnd.Silent;
            }

            if (now >= _nextSummary)
            {
                _nextSummary = now + SummaryInterval;
                LogSummary();
            }

            if (Interlocked.Exchange(ref _changed, 0) == 1 || now >= nextRecompute)
            {
                nextRecompute = now + RecomputeInterval;
                await RecomputeAsync(sync, link, cancellationToken);
                LogDropped();
            }
        }

        return ConnectionEnd.Closed;
    }

    /// <summary>True when market hours have no packet for longer than the silence limit on a connection with an index in its set.</summary>
    private bool IsSilent(Link link, ActiveSet? current, DateTimeOffset now) =>
        now - link.LastPacketAt > settings.Silence
        && TimeRange.MarketHours.Contains(TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime))
        && current is not null
        && current.Members.Any(m => m.Reasons.HasFlag(ActiveSetReason.Index));

    private async Task RecomputeAsync(SubscriptionSync sync, Link link, CancellationToken cancellationToken)
    {
        ActiveSet next;
        try
        {
            next = await sets.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the set stays as it is until the next look
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.ActiveSetFailed(logger, ex);
            return;
        }

        // Ticks of an added instrument can arrive as soon as its subscription is sent: until the difference is applied both the old and the new members are known.
        var before = link.Lookup;
        var wanted = Lookup(next);
        link.Lookup = new Dictionary<FeedSecurity, Guid>(before.Concat(wanted.Where(w => !before.ContainsKey(w.Key))));
        var diff = await sync.ApplyAsync(next, cancellationToken);
        link.Lookup = wanted;
        if (!diff.IsEmpty)
        {
            metrics.SetSubscribed(next.Members.Count);
            _state = next.Members.Count == 0 ? FeedStates.Idle : FeedStates.Connected;
            if (diff.Subscribe.Count > 0)
            {
                var quote = diff.Subscribe.Count(m => m.Mode == FeedMode.Quote);
                var messages = SubscriptionBatcher.Split(diff.Subscribe).Count;
                LiveFeedLog.Subscribed(logger, diff.Subscribe.Count, quote, diff.Subscribe.Count - quote, messages);
            }

            if (diff.Unsubscribe.Count > 0)
            {
                LiveFeedLog.Unsubscribed(logger, diff.Unsubscribe.Count);
            }
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        try
        {
            await flush.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // fail closed: no price is written, the connection stays, the next flush tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.FlushError(logger, ex);
        }

        metrics.SetFlushMs((int)clock.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>The ticks that arrived since the last flush are written when a connection ends (best effort, a few seconds at most).</summary>
    private async Task FinalFlushAsync()
    {
        using var wait = new CancellationTokenSource(FinalFlushWait, clock);
        try
        {
            await flush.FlushAsync(wait.Token);
        }
#pragma warning disable CA1031 // see FlushAsync
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.FlushError(logger, ex);
        }
    }

    /// <summary>Once a minute: the largest groups of dropped messages by response code and reason, with counts and lengths only (never the bytes).</summary>
    private void LogSummary()
    {
        var top = metrics.Packets.TopDropped(5);
        if (top.Count == 0)
        {
            return;
        }

        var text = string.Join("; ", top.Select(g => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"code={g.Code} reason={FeedPacketStats.ReasonName(g.Reason)} count={g.Count} lastLength={g.LastLength}")));
        LiveFeedLog.PacketSummary(logger, metrics.Packets.TotalDropped, text);
    }

    private void LogDropped()
    {
        var total = metrics.Dropped;
        if (total > _loggedDropped)
        {
            LiveFeedLog.PacketsDropped(logger, total - _loggedDropped, total);
            _loggedDropped = total;
        }
    }

    private static Dictionary<FeedSecurity, Guid> Lookup(ActiveSet set) => set.Members.ToDictionary(m => m.Key, m => m.InstrumentId);

    private enum ConnectionEnd
    {
        /// <summary>The server closed the connection or the stream ended (also a disconnect packet that is not about the token).</summary>
        Closed,
        TokenRefused,
        Silent,
        Failed,
    }

    /// <summary>What the reader and the maintenance loop share about one connection.</summary>
    private sealed class Link(DateTimeOffset connectedAt)
    {
        private long _lastPacketTicks = connectedAt.UtcTicks;

        private volatile IReadOnlyDictionary<FeedSecurity, Guid> _lookup = new Dictionary<FeedSecurity, Guid>();

        public IReadOnlyDictionary<FeedSecurity, Guid> Lookup
        {
            get => _lookup;
            set => _lookup = value;
        }

        public DateTimeOffset LastPacketAt => new(Interlocked.Read(ref _lastPacketTicks), TimeSpan.Zero);

        public void Touch(DateTimeOffset at) => Interlocked.Exchange(ref _lastPacketTicks, at.UtcTicks);
    }
}
