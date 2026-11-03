namespace XDrishti.Infrastructure.Market;

/// <summary>
/// The only endpoints the master data downloads from (ADR 0010, 0017): two public files and NSE's public trading-holiday list, GET only. Enforced for every request
/// of the master-data HTTP clients by <see cref="MasterDataEndpointGuard"/>. No credentials are ever sent.
/// </summary>
internal static class MasterDataEndpoints
{
    public static readonly Uri DhanScripMaster = new("https://images.dhan.co/api-data/api-scrip-master-detailed.csv");

    public static readonly Uri NseTotalMarketList = new("https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv");

    /// <summary>NSE's public trading-holiday list (JSON). The one endpoint that has a query: exactly <c>?type=trading</c>.</summary>
    public static readonly Uri NseHolidayMaster = new("https://www.nseindia.com/api/holiday-master?type=trading");

    private static readonly Uri[] Allowed = [DhanScripMaster, NseTotalMarketList, NseHolidayMaster];

    /// <summary>
    /// Dated archive paths (CF-04 Part A): a host plus a full-match path regular expression, GET, no query. <b>Deliberately empty</b>: this list is the owner's security act (ADR 0097, 0195). The app reads it and never edits it; until the owner
    /// adds the entries named in ADR 0195 (after reading NSE's terms), the history fetch is refused before anything is sent.
    /// </summary>
    public static readonly IReadOnlyList<AllowedPattern> Patterns = [];

    /// <summary>
    /// True only for GET on an exact HTTPS endpoint (host, default port, path) with no fragment or user info, and with no query — except for the holiday list, whose
    /// query must be exactly the allowed one.
    /// </summary>
    public static bool IsAllowed(HttpMethod? method, Uri? uri) => IsAllowed(method, uri, Patterns);

    /// <summary>As <see cref="IsAllowed(HttpMethod?, Uri?)"/> with the given path patterns (the tests pass their own; the guard uses <see cref="Patterns"/>).</summary>
    public static bool IsAllowed(HttpMethod? method, Uri? uri, IReadOnlyList<AllowedPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (method != HttpMethod.Get || uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        return Allowed.Any(a =>
            string.Equals(uri.Host, a.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.AbsolutePath, a.AbsolutePath, StringComparison.Ordinal)
            && string.Equals(uri.Query, a.Query, StringComparison.Ordinal))
            || (uri.Query.Length == 0 && patterns.Any(p => string.Equals(uri.Host, p.Host, StringComparison.OrdinalIgnoreCase) && p.Path.IsMatch(uri.AbsolutePath)));
    }
}
