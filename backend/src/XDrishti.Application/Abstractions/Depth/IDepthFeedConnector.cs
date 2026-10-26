using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Abstractions.Depth;

/// <summary>The result of a depth connection attempt: a connection, or a failure with a fixed safe message (never a token, a client id, a URL or an exception text).</summary>
public sealed record DepthConnectResult(IDepthFeedConnection? Connection, FeedConnectFailureKind? Failure = null, string? Message = null)
{
    public static DepthConnectResult Connected(IDepthFeedConnection connection) => new(connection);

    public static DepthConnectResult Failed(FeedConnectFailureKind kind, string message) => new(null, kind, message);
}

/// <summary>One open connection to Dhan's 20-level depth feed (P7-16). The implementation owns the socket and the parsing; a packet that cannot be parsed is counted, never thrown. Disposing closes the socket.</summary>
public interface IDepthFeedConnection : IAsyncDisposable
{
    /// <summary>The most instruments one connection carries (Dhan's limit).</summary>
    public const int MaxInstruments = 50;

    /// <summary>How many packets this connection dropped (wrong length, unknown code, segment or price).</summary>
    long DroppedPackets { get; }

    /// <summary>Why the stream ended, in a fixed safe word; empty while it goes on.</summary>
    string EndReason { get; }

    /// <summary>Subscribes to the securities (NSE equity, at most <see cref="MaxInstruments"/>) in one message.</summary>
    ValueTask SubscribeAsync(IReadOnlyList<int> securityIds, CancellationToken cancellationToken);

    /// <summary>The packets as they arrive; ends when the server closes the connection, the network fails or the token is cancelled.</summary>
    IAsyncEnumerable<DepthPacket> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Opens depth connections with the Data account's credentials. The implementation checks the endpoint against the allow-list before anything is opened.</summary>
public interface IDepthFeedConnector
{
    /// <summary>Connects. A refused token or an unreachable server is a result, not an exception; only cancellation throws.</summary>
    ValueTask<DepthConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken);
}
