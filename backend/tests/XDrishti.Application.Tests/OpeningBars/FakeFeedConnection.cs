using System.Runtime.CompilerServices;
using System.Threading.Channels;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>A feed connection the test feeds by hand; it records each subscribe message with its mode.</summary>
internal sealed class FakeFeedConnection : ILiveFeedConnection
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
