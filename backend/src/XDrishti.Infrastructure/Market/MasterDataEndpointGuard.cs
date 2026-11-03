namespace XDrishti.Infrastructure.Market;

/// <summary>Refuses, before anything is sent, every request of a master-data HTTP client that is not on <see cref="MasterDataEndpoints"/>' allow-list.</summary>
internal sealed class MasterDataEndpointGuard : DelegatingHandler
{
    private readonly IReadOnlyList<AllowedPattern> _patterns;

    public MasterDataEndpointGuard()
        : this(MasterDataEndpoints.Patterns)
    {
    }

    /// <summary>For tests: a guard with its own path patterns (the application always uses <see cref="MasterDataEndpoints.Patterns"/>).</summary>
    internal MasterDataEndpointGuard(IReadOnlyList<AllowedPattern> patterns) => _patterns = patterns;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MasterDataEndpoints.IsAllowed(request.Method, request.RequestUri, _patterns)
            ? base.SendAsync(request, cancellationToken)
            : throw MasterDataEndpointNotAllowedException.For(request);
    }
}
