namespace XDrishti.Infrastructure.Market;

/// <summary>Refuses, before anything is sent, every request of a master-data HTTP client that is not on <see cref="MasterDataEndpoints"/>' allow-list.</summary>
internal sealed class MasterDataEndpointGuard : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MasterDataEndpoints.IsAllowed(request.Method, request.RequestUri)
            ? base.SendAsync(request, cancellationToken)
            : throw MasterDataEndpointNotAllowedException.For(request);
    }
}
