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

    internal static BrokerEndpointNotAllowedException For(HttpRequestMessage request) =>
        new($"The broker endpoint {request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath} is not on the allow-list.");
}
