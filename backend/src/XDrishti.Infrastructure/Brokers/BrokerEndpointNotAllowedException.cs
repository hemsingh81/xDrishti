namespace XDrishti.Infrastructure.Brokers;

/// <summary>A broker HTTP client was asked to call an endpoint outside the allow-list (a programming error). The message names method, host and path only.</summary>
public sealed class BrokerEndpointNotAllowedException : InvalidOperationException
{
    public BrokerEndpointNotAllowedException()
        : this("A broker endpoint outside the allow-list was requested.")
    {
    }

    public BrokerEndpointNotAllowedException(string message)
        : base(message)
    {
    }

    public BrokerEndpointNotAllowedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>For a WebSocket endpoint outside the allow-list: names the scheme, host and path only - never the query, which carries the token.</summary>
    internal static BrokerEndpointNotAllowedException ForSocket(Uri? uri) =>
        new(uri is { IsAbsoluteUri: true }
            ? $"The WebSocket endpoint {uri.Scheme}://{uri.Host}{uri.AbsolutePath} is not on the allow-list."
            : "A WebSocket endpoint outside the allow-list was requested.");

    internal static BrokerEndpointNotAllowedException For(HttpRequestMessage request) =>
        new($"The broker endpoint {request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath} is not on the allow-list.");
}
