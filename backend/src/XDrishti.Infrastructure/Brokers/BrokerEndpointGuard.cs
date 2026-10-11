namespace XDrishti.Infrastructure.Brokers;

/// <summary>Refuses, before anything is sent, every request of a broker HTTP client that is not on <see cref="BrokerEndpoints"/>' allow-list.</summary>
internal sealed class BrokerEndpointGuard : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BrokerEndpoints.IsAllowed(request.Method, request.RequestUri)
            ? base.SendAsync(request, cancellationToken)
            : throw BrokerEndpointNotAllowedException.For(request);
    }
}
