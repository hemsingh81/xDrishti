using System.Runtime.CompilerServices;
using System.Threading.Channels;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Dhan's market-data socket for tests: the feed connects to this instead (the real client is never started). The test pushes packets in and reads what the feed subscribed to; <see cref="Reset"/> between tests.
/// </summary>
public sealed class FakeFeedSocket : ILiveFeedConnector
{
    private readonly object _gate = new();
    private readonly Channel<bool> _subscriptions = Channel.CreateUnbounded<bool>();
    private readonly List<(FeedMode Mode, FeedSecurity[] Securities)> _subscribed = [];
    private Channel<FeedPacket> _packets = Channel.CreateUnbounded<FeedPacket>();

    /// <summary>When set, the next connects are refused as a refused token.</summary>
    public bool RefuseToken { get; set; }

    public int Connects { get; private set; }

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

    public void Push(FeedPacket packet) => _packets.Writer.TryWrite(packet);

    /// <summary>Completes when the feed has sent at least this many subscribe messages.</summary>
    public async Task WaitForSubscriptionsAsync(int messages, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        while (Subscribed.Count < messages)
        {
            await _subscriptions.Reader.ReadAsync(limit.Token);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _subscribed.Clear();
            Connects = 0;
            RefuseToken = false;
            _packets = Channel.CreateUnbounded<FeedPacket>();
        }
    }

    public ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Connects++;
            return ValueTask.FromResult(RefuseToken
                ? FeedConnectResult.Failed(FeedConnectFailureKind.TokenRefused, "refused")
                : FeedConnectResult.Connected(new Connection(this, _packets)));
        }
    }

    private sealed class Connection(FakeFeedSocket owner, Channel<FeedPacket> packets) : ILiveFeedConnection
    {
        public long DroppedPackets => 0;

        public string EndReason => string.Empty;

        public ValueTask SubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken)
        {
            lock (owner._gate)
            {
                owner._subscribed.Add((mode, [.. securities]));
            }

            owner._subscriptions.Writer.TryWrite(true);

            return ValueTask.CompletedTask;
        }

        public ValueTask UnsubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<FeedPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                var more = false;
                try
                {
                    more = await packets.Reader.WaitToReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // a cancelled read just ends, like the real connection
                }

                if (!more)
                {
                    yield break;
                }

                while (packets.Reader.TryRead(out var packet))
                {
                    yield return packet;
                }
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
