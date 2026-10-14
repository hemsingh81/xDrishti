namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// One open connection to Dhan's market feed (ADR 0024): subscribe and unsubscribe in batches and read the parsed packets. The implementation owns the socket, the parsing and the answering of pings; a packet that cannot be parsed is
/// counted (<see cref="DroppedPackets"/>), never thrown. At most one connection exists at a time (the manager's rule). Disposing closes the socket.
/// </summary>
public interface ILiveFeedConnection : IAsyncDisposable
{
    /// <summary>The most instruments one subscribe or unsubscribe message may carry (Dhan's limit).</summary>
    public const int MaxBatch = 100;

    /// <summary>How many packets this connection dropped (unknown code, wrong length, bad price or time).</summary>
    long DroppedPackets { get; }

    /// <summary>Why the stream of packets ended, in a fixed safe word (never a URL, a token or an exception text); empty while it goes on.</summary>
    string EndReason { get; }

    /// <summary>Adds the securities (at most <see cref="MaxBatch"/>) to the feed in the mode: one message.</summary>
    ValueTask SubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken);

    /// <summary>Removes the securities (at most <see cref="MaxBatch"/>) that were subscribed in the mode: one message.</summary>
    ValueTask UnsubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken);

    /// <summary>The packets as they arrive. Ends when the server closes the connection or the network fails (<see cref="EndReason"/> says which) and when <paramref name="cancellationToken"/> is cancelled.</summary>
    IAsyncEnumerable<FeedPacket> ReadAsync(CancellationToken cancellationToken);
}
