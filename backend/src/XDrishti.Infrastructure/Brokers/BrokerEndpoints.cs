namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The only broker endpoints xDrishti calls (ADR 0008, 0009, 0012, 0016, 0025): Dhan's consent-flow authentication, the read of the account's holdings
/// (<c>GET /v2/holdings</c>, no query), the trade history (<c>GET /v2/trades/{from}/{to}/{page}</c>, dates in the path, no query) and the ledger (<c>GET /v2/ledger?from-date=…&amp;to-date=…</c>, exactly those two query keys), the 1-minute chart data of the Data API (<c>POST /v2/charts/intraday</c>, no query) and the market quote of the holdings
/// (<c>POST /v2/marketfeed/quote</c>, no query; ADR 0019) — never orders, positions or funds, and no other market-feed path (<c>ltp</c>, <c>ohlc</c>). Enforced for every request of the broker HTTP clients by <see cref="BrokerEndpointGuard"/>. The two login pages are opened by the owner's
/// browser, never by the server.
/// </summary>
internal static class BrokerEndpoints
{
    public static readonly Uri DhanGenerateConsent = new("https://auth.dhan.co/app/generate-consent");

    public static readonly Uri DhanConsumeConsent = new("https://auth.dhan.co/app/consumeApp-consent");

    /// <summary>The account's holdings (header <c>access-token</c>); the only data endpoint that is allowed, and it takes no query.</summary>
    public static readonly Uri DhanHoldings = new("https://api.dhan.co/v2/holdings");

    /// <summary>The account's trades between two dates, one page at a time (ADR 0025; header <c>access-token</c>): <c>/v2/trades/{yyyy-MM-dd}/{yyyy-MM-dd}/{page}</c>, pages from 0, no query. Read-only; built by <see cref="BuildTradeHistoryUri"/>.</summary>
    public static readonly Uri DhanTradeHistoryBase = new("https://api.dhan.co/v2/trades");

    /// <summary>The account's ledger between two dates (ADR 0025; header <c>access-token</c>): <c>/v2/ledger</c> with exactly the query keys <see cref="LedgerFromKey"/> and <see cref="LedgerToKey"/>. Read-only; built by <see cref="BuildLedgerUri"/>.</summary>
    public static readonly Uri DhanLedgerBase = new("https://api.dhan.co/v2/ledger");

    public const string LedgerFromKey = "from-date";

    public const string LedgerToKey = "to-date";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>More pages than any window has (the reader stops at 200); a bigger number in a path is refused.</summary>
    private const int MaxPageNumber = 10_000;

    /// <summary>1-minute bars of an instrument for a period of at most 90 days (JSON body, header <c>access-token</c>; ADR 0016 §7). Read-only despite the POST; no other chart path is allowed.</summary>
    public static readonly Uri DhanIntradayChart = new("https://api.dhan.co/v2/charts/intraday");

    /// <summary>The market quote of up to 1,000 securities (JSON body <c>{"NSE_EQ":[ids]}</c>, headers <c>access-token</c> and <c>client-id</c>; ADR 0019 §1). Read-only despite the POST; <c>ltp</c>, <c>ohlc</c> and every other market-feed path stay refused.</summary>
    public static readonly Uri DhanMarketQuote = new("https://api.dhan.co/v2/marketfeed/quote");

    /// <summary>
    /// Dhan's market-data WebSocket (ADR 0024 §8; unverified until the owner's first real session): <c>wss://api-feed.dhan.co</c> with exactly the query keys <see cref="FeedQueryKeys"/> (<c>version</c>, <c>token</c>, <c>clientId</c>,
    /// <c>authType</c>). The only socket the application may open, checked by <see cref="IsAllowedWebSocket"/> before the connection is made.
    /// </summary>
    public static readonly Uri DhanFeed = new("wss://api-feed.dhan.co");

    /// <summary>The query keys of the feed URL, each exactly once and nothing else.</summary>
    public static readonly IReadOnlyList<string> FeedQueryKeys = ["version", "token", "clientId", "authType"];

    /// <summary>Browser page: Dhan's consent login (query <c>consentAppId</c>).</summary>
    public const string DhanLoginPage = "https://auth.dhan.co/login/consentApp-login";

    /// <summary>Method, endpoint and the one query parameter it may carry (none: no query at all).</summary>
    private static readonly (HttpMethod Method, Uri Endpoint, string? QueryParameter)[] Allowed =
    [
        (HttpMethod.Post, DhanGenerateConsent, "client_id"),
        (HttpMethod.Post, DhanConsumeConsent, "tokenId"),
        (HttpMethod.Get, DhanHoldings, null),
        (HttpMethod.Post, DhanIntradayChart, null),
        (HttpMethod.Post, DhanMarketQuote, null),
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

        return method == HttpMethod.Get && string.Equals(uri.Host, DhanHoldings.Host, StringComparison.OrdinalIgnoreCase) && (IsTradeHistory(uri) || IsLedger(uri));
    }

    /// <summary>The URL of one page of the trade history: <c>https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0</c>. Dates are written the same way whatever the culture; the result is never logged.</summary>
    public static Uri BuildTradeHistoryUri(DateOnly from, DateOnly to, int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        return new Uri(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{DhanTradeHistoryBase.AbsoluteUri}/{from.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture)}/{to.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture)}/{page}"));
    }

    /// <summary>The URL of the ledger of a period: <c>https://api.dhan.co/v2/ledger?from-date=2026-09-01&amp;to-date=2026-09-30</c>.</summary>
    public static Uri BuildLedgerUri(DateOnly from, DateOnly to) =>
        new(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{DhanLedgerBase.AbsoluteUri}?{LedgerFromKey}={from.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture)}&{LedgerToKey}={to.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary><c>/v2/trades/{date}/{date}/{page}</c> with two valid dates and a page number of digits only, and no query at all (not even an empty one).</summary>
    private static bool IsTradeHistory(Uri uri)
    {
        if (uri.Query.Length > 0 || uri.OriginalString.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = DhanTradeHistoryBase.AbsolutePath + "/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = uri.AbsolutePath[prefix.Length..].Split('/');
        return parts.Length == 3
            && IsDate(parts[0])
            && IsDate(parts[1])
            && parts[2].Length is > 0 and <= 5
            && parts[2].All(char.IsAsciiDigit)
            && int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) <= MaxPageNumber;
    }

    /// <summary><c>/v2/ledger</c> with a query of exactly the two keys <c>from-date</c> and <c>to-date</c> - each once, in either order, spelled exactly - with valid dates, and nothing else.</summary>
    private static bool IsLedger(Uri uri)
    {
        if (!string.Equals(uri.AbsolutePath, DhanLedgerBase.AbsolutePath, StringComparison.Ordinal) || uri.Query.Length < 2 || uri.Query[0] != '?')
        {
            return false;
        }

        var pairs = uri.Query[1..].Split('&');
        if (pairs.Length != 2)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || !IsDate(pair[(separator + 1)..]) || pair[..separator] is not (LedgerFromKey or LedgerToKey) || !seen.Add(pair[..separator]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDate(string text) =>
        text.Length == DateFormat.Length && DateOnly.TryParseExact(text, DateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    /// <summary>The URL of the feed for a Data account: <c>wss://api-feed.dhan.co?version=2&amp;token=…&amp;clientId=…&amp;authType=2</c>. The values are escaped; the result is never logged.</summary>
    public static Uri BuildFeedUri(string clientId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new Uri($"{DhanFeed.Scheme}://{DhanFeed.Host}?version=2&token={Uri.EscapeDataString(token)}&clientId={Uri.EscapeDataString(clientId)}&authType=2");
    }

    /// <summary>
    /// True only for the exact feed endpoint: scheme <c>wss</c>, host <c>api-feed.dhan.co</c>, the default port, an empty or root path, no user info or fragment, and a query of exactly the keys <see cref="FeedQueryKeys"/> - each once, with
    /// a value, in any order, spelled exactly (case matters) - and nothing else.
    /// </summary>
    public static bool IsAllowedWebSocket(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != "wss" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0
            || !string.Equals(uri.Host, DhanFeed.Host, StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath != "/")
        {
            return false;
        }

        if (uri.OriginalString.Contains('#', StringComparison.Ordinal) || uri.Query.Length < 2 || uri.Query[0] != '?')
        {
            return false;
        }

        var keys = new List<string>();
        foreach (var pair in uri.Query[1..].Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == pair.Length - 1)
            {
                return false;
            }

            keys.Add(pair[..separator]);
        }

        return keys.Count == FeedQueryKeys.Count && keys.Order(StringComparer.Ordinal).SequenceEqual(FeedQueryKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
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
