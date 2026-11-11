using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

/// <summary>
/// The allow-list of the tool server's one HTTP client (BK-12b AC 9): before anything is sent, a request that is not a GET of exactly <c>/api/reports/baskets/{guid}/profile</c> or <c>/api/reports/baskets/ranking</c> on the configured host is refused. The token never travels anywhere else.
/// </summary>
internal sealed partial class BasketApiEndpointGuard(IOptions<BasketApiOptions> options) : DelegatingHandler
{
    /// <summary>The two path templates the client may call: the whole allow-list.</summary>
    internal static readonly string[] PathTemplates = ["/api/reports/baskets/{id}/profile", "/api/reports/baskets/ranking"];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsAllowed(request.Method, request.RequestUri, options.Value.BaseUrl)
            ? base.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException("The basket API client may only GET the two basket report routes.");
    }

    internal static bool IsAllowed(HttpMethod? method, Uri? uri, string baseUrl) =>
        method == HttpMethod.Get
        && uri is { IsAbsoluteUri: true, UserInfo.Length: 0, Fragment.Length: 0 }
        && Uri.TryCreate(baseUrl, UriKind.Absolute, out var allowed)
        && uri.Scheme == allowed.Scheme && uri.Port == allowed.Port && string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
        && (ProfilePath().IsMatch(uri.AbsolutePath) || uri.AbsolutePath == "/api/reports/baskets/ranking");

    [GeneratedRegex("^/api/reports/baskets/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/profile$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProfilePath();
}
