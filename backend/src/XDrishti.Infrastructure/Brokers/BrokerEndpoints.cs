namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The only broker endpoints xDrishti calls (ADR 0008, 0009, 0012, 0016): Dhan's consent-flow authentication, the read of the account's holdings
/// (<c>GET /v2/holdings</c>, no query) and the 1-minute chart data of the Data API (<c>POST /v2/charts/intraday</c>, no query) — never orders, positions or funds. Enforced for every request of the broker HTTP clients by <see cref="BrokerEndpointGuard"/>. The two login pages are opened by the owner's
/// browser, never by the server.
/// </summary>
internal static class BrokerEndpoints
{
    public static readonly Uri DhanGenerateConsent = new("https://auth.dhan.co/app/generate-consent");

    public static readonly Uri DhanConsumeConsent = new("https://auth.dhan.co/app/consumeApp-consent");

    /// <summary>The account's holdings (header <c>access-token</c>); the only data endpoint that is allowed, and it takes no query.</summary>
    public static readonly Uri DhanHoldings = new("https://api.dhan.co/v2/holdings");

    /// <summary>1-minute bars of an instrument for a period of at most 90 days (JSON body, header <c>access-token</c>; ADR 0016 §7). Read-only despite the POST; no other chart path is allowed.</summary>
    public static readonly Uri DhanIntradayChart = new("https://api.dhan.co/v2/charts/intraday");

    /// <summary>Browser page: Dhan's consent login (query <c>consentAppId</c>).</summary>
    public const string DhanLoginPage = "https://auth.dhan.co/login/consentApp-login";

    /// <summary>Method, endpoint and the one query parameter it may carry (none: no query at all).</summary>
    private static readonly (HttpMethod Method, Uri Endpoint, string? QueryParameter)[] Allowed =
    [
        (HttpMethod.Post, DhanGenerateConsent, "client_id"),
        (HttpMethod.Post, DhanConsumeConsent, "tokenId"),
        (HttpMethod.Get, DhanHoldings, null),
        (HttpMethod.Post, DhanIntradayChart, null),
    ];

    /// <summary>True only for an allowed method and exact HTTPS endpoint (host, default port, path) with nothing but its own query parameter.</summary>
    public static bool IsAllowed(HttpMethod? method, Uri? uri)
    {
        if (method is null || uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        foreach (var (allowedMethod, endpoint, parameter) in Allowed)
        {
            if (method == allowedMethod
                && string.Equals(uri.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.AbsolutePath, endpoint.AbsolutePath, StringComparison.Ordinal)
                && QueryIsAllowed(uri, parameter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool QueryIsAllowed(Uri uri, string? parameter)
    {
        var query = uri.Query;
        if (parameter is null)
        {
            // Not even an empty query ("…/holdings?").
            return query.Length == 0 && !uri.OriginalString.Contains('?', StringComparison.Ordinal);
        }

        if (query.Length == 0)
        {
            return true;
        }

        var prefix = "?" + parameter + "=";
        return query.StartsWith(prefix, StringComparison.Ordinal) && query.IndexOfAny(['&', '#'], prefix.Length) < 0;
    }
}
