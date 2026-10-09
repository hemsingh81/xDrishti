namespace XDrishti.DhanTrial;

/// <summary>
/// Allow-list of the only endpoints this tool may call: authentication, profile, instrument master and historical candles.
/// Orders, positions, funds and IP-whitelisting endpoints are unreachable by construction (acceptance criterion 8).
/// </summary>
internal static class EndpointPolicy
{
    private static readonly (HttpMethod Method, string Host, string Path)[] Allowed =
    [
        (HttpMethod.Post, "auth.dhan.co", "/app/generate-consent"),
        (HttpMethod.Get, "auth.dhan.co", "/app/consumeApp-consent"),
        (HttpMethod.Post, "auth.dhan.co", "/app/consumeApp-consent"),
        (HttpMethod.Post, "auth.dhan.co", "/app/generateAccessToken"),
        (HttpMethod.Get, "api.dhan.co", "/v2/RenewToken"),
        (HttpMethod.Get, "api.dhan.co", "/v2/profile"),
        (HttpMethod.Post, "api.dhan.co", "/v2/charts/intraday"),
        (HttpMethod.Post, "api.dhan.co", "/v2/charts/historical"),
        (HttpMethod.Get, "images.dhan.co", "/api-data/api-scrip-master.csv"),
    ];

    public static bool IsAllowed(HttpMethod method, Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return Allowed.Any(a => a.Method == method
            && string.Equals(a.Host, uri.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Path, uri.AbsolutePath, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Rejects any request outside <see cref="EndpointPolicy"/> before it leaves the machine.</summary>
internal sealed class ReadOnlyGuardHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!EndpointPolicy.IsAllowed(request.Method, request.RequestUri))
        {
            throw new InvalidOperationException($"Blocked: {request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath} is not on the read-only allow-list.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
