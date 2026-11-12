using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

namespace XDrishti.Infrastructure.Ai.ReadOnly.EvidenceApi;

/// <summary>
/// The allow-list of the evidence client (AI-01, ADR 0280): before anything is sent, a request that is not a GET of one of the five evidence paths on the configured host is refused. The token never travels anywhere else.
/// </summary>
internal sealed partial class EvidenceApiEndpointGuard(IOptions<BasketApiOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsAllowed(request.Method, request.RequestUri, options.Value.BaseUrl)
            ? base.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException("The evidence API client may only GET the evidence routes.");
    }

    internal static bool IsAllowed(HttpMethod? method, Uri? uri, string baseUrl) =>
        method == HttpMethod.Get
        && uri is { IsAbsoluteUri: true, UserInfo.Length: 0, Fragment.Length: 0 }
        && Uri.TryCreate(baseUrl, UriKind.Absolute, out var allowed)
        && uri.Scheme == allowed.Scheme && uri.Port == allowed.Port && string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
        && (uri.AbsolutePath is "/api/replay/runs" or "/api/confidence/scorecards" or "/api/paper/shadow/summary" || RunPath().IsMatch(uri.AbsolutePath) || CardPath().IsMatch(uri.AbsolutePath));

    [GeneratedRegex("^/api/replay/runs/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RunPath();

    [GeneratedRegex("^/api/confidence/scorecards/[A-Za-z0-9_.-]{1,60}/versions/[0-9]{1,6}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CardPath();
}
