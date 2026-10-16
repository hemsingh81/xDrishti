using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;

namespace XDrishti.Application.Live.Push;

/// <summary>
/// The hub's push (ADR 0024 §7, P2-10a), a singleton for the lifetime of <c>xd-api</c>. It knows the connected screens (<see cref="PushConnection"/>) and
/// <list type="bullet">
/// <item>on <c>NOTIFY live_prices</c> (<see cref="ILivePriceChanges"/>), after a subscribe, and at least every <see cref="PushSettings.SweepInterval"/>, reads the current prices of the union of the followed instruments once
/// (<see cref="ILivePriceReader"/>) and sends each connection only the followed instruments whose price differs from what it was last sent, in messages bounded by <see cref="PriceMessageSplitter"/>. A burst of notifications is folded: the next
/// push waits <see cref="PushSettings.MinPushInterval"/> after the last, so a screen gets at most one burst a second;</item>
/// <item>every <see cref="PushSettings.StatusPollInterval"/> looks at the feed's heartbeat and sends the status to all connections when it changed and at least every <see cref="PushSettings.StatusRefreshInterval"/>; a new connection gets it at once;</item>
/// <item>every <see cref="PushSettings.SessionCheckInterval"/> closes the connections whose owner session ended.</item>
/// </list>
/// All database work uses a scope per step. Nothing here depends on the feed process: with the feed down the status says so and prices still come from quotes and syncs.
/// </summary>
internal sealed class LivePushCoordinator(
    IServiceScopeFactory scopes,
    ILiveClients clients,
    ILivePriceChanges changes,
    PushSettings settings,
    TimeProvider clock,
    ILogger<LivePushCoordinator> logger) : ILiveSubscriptions, ILivePushLoop
{
    private readonly ConcurrentDictionary<string, PushConnection> _connections = new(StringComparer.Ordinal);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private LiveFeedStatus? _lastStatus;
    private DateTimeOffset _lastStatusAt;

    public int ConnectionCount => _connections.Count;

    // ---- the hub's side ----

    public async ValueTask ConnectedAsync(string connectionId, Guid sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        var connection = new PushConnection(connectionId, sessionId);
        _connections[connectionId] = connection;
        LivePushLog.Connected(logger, _connections.Count);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLiveStatusQuery, LiveFeedStatus>>().HandleAsync(new GetLiveStatusQuery(), cancellationToken);
            await clients.SendStatusAsync(connectionId, status, cancellationToken);
        }
#pragma warning disable CA1031 // a status that cannot be read or sent must not fail the connection; the next status push tries again
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LivePushLog.StatusFailed(logger, ex.GetType().Name);
        }
    }

    public async ValueTask<SubscribeAck> SubscribeAsync(string connectionId, LiveScope? scope, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            return new SubscribeAck(0, false, false);
        }

        var ids = scope?.HoldingIds ?? [];
        var truncated = ids.Count > settings.MaxHoldingIds;
        IReadOnlyList<Guid> instruments;
        try
        {
            await using var work = scopes.CreateAsyncScope();
            instruments = await work.ServiceProvider.GetRequiredService<ILiveScopeResolver>()
                .ResolveAsync(scope?.AccountId, scope?.BucketId, [.. ids.Take(settings.MaxHoldingIds)], cancellationToken);
        }
#pragma warning disable CA1031 // an unreadable scope is answered as failed, never thrown at the hub
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LivePushLog.SubscribeFailed(logger, ex.GetType().Name);
            connection.Unfollow();
            return new SubscribeAck(0, false, Failed: true);
        }

        if (instruments.Count > settings.MaxInstrumentsPerConnection)
        {
            truncated = true;
            instruments = [.. instruments.Take(settings.MaxInstrumentsPerConnection)];
        }

        connection.Follow(instruments);
        LivePushLog.Subscribed(logger, instruments.Count, truncated);
        Wake();
        return new SubscribeAck(instruments.Count, truncated, false);
    }

    public void Unsubscribe(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.Unfollow();
        }
    }

    public void Disconnected(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            LivePushLog.Disconnected(logger, _connections.Count);
        }
    }

    // ---- the loops ----

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var watching = changes.WatchAsync(Wake, cancellationToken);
        await Task.WhenAll(watching, PriceLoopAsync(cancellationToken), StatusLoopAsync(cancellationToken), SessionLoopAsync(cancellationToken));
    }

    /// <summary>Asks for a price push (a notification, a subscribe). Many calls before the push count as one.</summary>
    internal void Wake() => _wake.Writer.TryWrite(true);

    internal async Task PriceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WaitForWakeOrSweepAsync(cancellationToken);
                await PushPricesAsync(cancellationToken);
                await Task.Delay(settings.MinPushInterval, clock, cancellationToken); // notifications that come in now are folded into the next push
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // the loop must outlive any failure; it is logged by kind and tried again at the next notification or sweep
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LivePushLog.PushFailed(logger, ex.GetType().Name);
                await PauseAsync(cancellationToken);
            }
        }
    }

    private async Task WaitForWakeOrSweepAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var woken = _wake.Reader.WaitToReadAsync(stop.Token).AsTask();
        var swept = Task.Delay(settings.SweepInterval, clock, stop.Token);
        var first = await Task.WhenAny(woken, swept);
        await stop.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (first == woken)
        {
            _wake.Reader.TryRead(out _); // the sweep losing is cancelled; a wake that came in meanwhile stays for the next round
        }
    }

    private async Task StatusLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(settings.StatusPollInterval, clock, cancellationToken);
                await PushStatusAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // see PriceLoopAsync
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LivePushLog.StatusFailed(logger, ex.GetType().Name);
            }
        }
    }

    private async Task SessionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(settings.SessionCheckInterval, clock, cancellationToken);
                await EndSessionsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // see PriceLoopAsync
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LivePushLog.SessionCheckFailed(logger, ex.GetType().Name);
            }
        }
    }

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(settings.MinPushInterval, clock, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    // ---- one step of each loop (the tests call these) ----

    /// <summary>Reads the prices of everything followed and sends each connection what it has not got.</summary>
    internal async Task PushPricesAsync(CancellationToken cancellationToken)
    {
        var following = _connections.Values.Select(c => (Connection: c, Instruments: c.Following)).Where(c => c.Instruments.Count > 0).ToList();
        if (following.Count == 0)
        {
            return;
        }

        var union = following.SelectMany(c => c.Instruments).ToHashSet();
        IReadOnlyList<InstrumentPrice> read;
        await using (var scope = scopes.CreateAsyncScope())
        {
            read = await scope.ServiceProvider.GetRequiredService<ILivePriceReader>().ReadAsync(union, cancellationToken);
        }

        var current = read.ToDictionary(p => p.InstrumentId);
        var asOf = clock.GetUtcNow();
        int instruments = 0, messageCount = 0, connectionCount = 0;
        foreach (var (connection, _) in following)
        {
            var changed = connection.TakeChanged(current);
            if (changed.Count == 0)
            {
                continue;
            }

            connectionCount++;
            foreach (var message in PriceMessageSplitter.Split(changed, asOf, settings.MaxInstrumentsPerMessage, settings.MaxMessageBytes))
            {
                if (await TrySendAsync(connection, message, cancellationToken))
                {
                    connection.MarkSent(message.Instruments); // only what was delivered; a failed message is offered again at the next push
                    instruments += message.Instruments.Count;
                    messageCount++;
                }
            }
        }

        if (messageCount > 0)
        {
            LivePushLog.Pushed(logger, connectionCount, instruments, messageCount);
        }
    }

    private async ValueTask<bool> TrySendAsync(PushConnection connection, PricesChanged message, CancellationToken cancellationToken)
    {
        try
        {
            await clients.SendPricesAsync(connection.Id, message, cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // a connection that went away must not stop the push to the others
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LivePushLog.SendFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Sends the feed status to every connection when it changed or the refresh is due.</summary>
    internal async Task PushStatusAsync(CancellationToken cancellationToken)
    {
        if (_connections.IsEmpty)
        {
            return;
        }

        LiveFeedStatus status;
        await using (var scope = scopes.CreateAsyncScope())
        {
            status = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLiveStatusQuery, LiveFeedStatus>>().HandleAsync(new GetLiveStatusQuery(), cancellationToken);
        }

        var now = clock.GetUtcNow();
        var changed = _lastStatus is null || _lastStatus.State != status.State || _lastStatus.Subscribed != status.Subscribed;
        if (!changed && now - _lastStatusAt < settings.StatusRefreshInterval)
        {
            return;
        }

        _lastStatus = status;
        _lastStatusAt = now;
        foreach (var connection in _connections.Values)
        {
            try
            {
                await clients.SendStatusAsync(connection.Id, status, cancellationToken);
            }
#pragma warning disable CA1031 // see TrySendAsync
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LivePushLog.SendFailed(logger, ex.GetType().Name);
            }
        }
    }

    /// <summary>Closes the connections whose session ended.</summary>
    internal async Task EndSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = _connections.Values.Select(c => c.SessionId).Distinct().ToList();
        if (sessions.Count == 0)
        {
            return;
        }

        IReadOnlyList<Guid> ended;
        await using (var scope = scopes.CreateAsyncScope())
        {
            ended = await scope.ServiceProvider.GetRequiredService<IQueryHandler<ListEndedSessionsQuery, IReadOnlyList<Guid>>>()
                .HandleAsync(new ListEndedSessionsQuery(sessions), cancellationToken);
        }

        if (ended.Count == 0)
        {
            return;
        }

        var closing = _connections.Values.Where(c => ended.Contains(c.SessionId)).ToList();
        foreach (var connection in closing)
        {
            _connections.TryRemove(connection.Id, out _);
            clients.Disconnect(connection.Id);
        }

        LivePushLog.SessionEnded(logger, closing.Count);
    }
}
