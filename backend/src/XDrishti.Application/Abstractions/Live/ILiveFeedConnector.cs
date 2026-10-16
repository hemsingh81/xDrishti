using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Abstractions.Live;

/// <summary>Why a connection could not be made.</summary>
public enum FeedConnectFailureKind
{
    /// <summary>The network, a timeout or a server error: the manager tries again after its back-off.</summary>
    Failed,

    /// <summary>Dhan refused the Data account's token (401 or 403 on the upgrade): reconnecting cannot work until the token changes.</summary>
    TokenRefused,
}

/// <summary>The result of a connection attempt: a connection, or a failure with a fixed safe message (never a token, a client id, a URL or an exception text).</summary>
public sealed record FeedConnectResult(ILiveFeedConnection? Connection, FeedConnectFailureKind? Failure = null, string? Message = null)
{
    public static FeedConnectResult Connected(ILiveFeedConnection connection) => new(connection);

    public static FeedConnectResult Failed(FeedConnectFailureKind kind, string message) => new(null, kind, message);
}

/// <summary>Opens connections to Dhan's market feed with the Data account's credentials (so that the manager can reconnect). The implementation checks the endpoint against the allow-list before anything is opened.</summary>
public interface ILiveFeedConnector
{
    /// <summary>Connects. A refused token or an unreachable server is a result, not an exception; only cancellation throws.</summary>
    ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken);
}
