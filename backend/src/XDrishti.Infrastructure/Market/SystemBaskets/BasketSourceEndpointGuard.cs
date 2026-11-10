using System.Text.RegularExpressions;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>
/// Refuses, before anything is sent, every request of the system-basket client that is not a GET of one index list file or one of the two F&amp;O files (BK-05: <c>/content/fo/fo_secban.csv</c> and <c>fo_mktlots.csv</c>): HTTPS, default port, no query, no user info, on the two index file hosts, a plain <c>.csv</c> file name under their index folder.
/// Never <c>www.nseindia.com</c> or any <c>/api/*</c> path (BK-01).
/// </summary>
internal sealed partial class BasketSourceEndpointGuard : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsAllowed(request.Method, request.RequestUri) ? base.SendAsync(request, cancellationToken) : throw MasterDataEndpointNotAllowedException.For(request);
    }

    internal static bool IsAllowed(HttpMethod? method, Uri? uri) =>
        method == HttpMethod.Get && uri is { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true, UserInfo.Length: 0, Fragment.Length: 0, Query.Length: 0 }
        && ((uri.Host.Equals("nsearchives.nseindia.com", StringComparison.OrdinalIgnoreCase) && (ArchivePath().IsMatch(uri.AbsolutePath) || FoPath().IsMatch(uri.AbsolutePath)))
            || (uri.Host.Equals("niftyindices.com", StringComparison.OrdinalIgnoreCase) && IndicesPath().IsMatch(uri.AbsolutePath)));

    [GeneratedRegex("^/content/indices/[A-Za-z0-9_]{1,80}\\.csv$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ArchivePath();

    [GeneratedRegex("^/content/fo/(fo_secban|fo_mktlots)\\.csv$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FoPath();

    [GeneratedRegex("^/IndexConstituent/[A-Za-z0-9_]{1,80}\\.csv$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IndicesPath();
}
