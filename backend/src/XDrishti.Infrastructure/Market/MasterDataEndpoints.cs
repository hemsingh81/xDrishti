namespace XDrishti.Infrastructure.Market;

/// <summary>
/// The only endpoints the instrument master downloads from (ADR 0010): two public files, GET only. Enforced for every request of the master-data
/// HTTP clients by <see cref="MasterDataEndpointGuard"/>. No credentials are ever sent.
/// </summary>
internal static class MasterDataEndpoints
{
    public static readonly Uri DhanScripMaster = new("https://images.dhan.co/api-data/api-scrip-master-detailed.csv");

    public static readonly Uri NseTotalMarketList = new("https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv");

    private static readonly Uri[] Allowed = [DhanScripMaster, NseTotalMarketList];

    /// <summary>True only for GET on an exact HTTPS endpoint (host, default port, path) with no query, fragment or user info.</summary>
    public static bool IsAllowed(HttpMethod? method, Uri? uri)
    {
        if (method != HttpMethod.Get || uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.Query.Length > 0)
        {
            return false;
        }

        return Allowed.Any(a =>
            string.Equals(uri.Host, a.Host, StringComparison.OrdinalIgnoreCase) && string.Equals(uri.AbsolutePath, a.AbsolutePath, StringComparison.Ordinal));
    }
}
