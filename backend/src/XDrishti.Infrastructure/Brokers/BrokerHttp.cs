using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Sends one request to Dhan and turns the answer into JSON or a <see cref="BrokerFailure"/>: 2xx → the JSON body; 429 or a 4xx with DH-904 → rate limited; other 4xx → rejected;
/// anything else (3xx — redirects are not followed —, 5xx, timeout, network failure, unreadable body) → unavailable. Never retries: codes are
/// single-use. Only the operation, the HTTP status and Dhan's error code are logged — never URLs, headers or bodies.
/// </summary>
internal static partial class BrokerHttp
{
    /// <summary>Upper bound for one broker call (the spec: 15 seconds).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Dhan's error code for an invalid or expired access token (documented authentication error).</summary>
    public const string TokenRefusedCode = "DH-901";

    /// <summary>Dhan's code for "too many requests".</summary>
    public const string RateLimitCode = "DH-904";

    /// <summary>Broker answers are small; anything bigger is refused.</summary>
    public const int MaxResponseBytes = 256 * 1024;

    public static void Configure(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = Timeout;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Upper bound for the holdings read (the spec: 30 seconds).</summary>
    public static readonly TimeSpan HoldingsTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A list of holdings is bigger than a token answer, but anything beyond 5 MB is refused.</summary>
    public const int MaxHoldingsResponseBytes = 5 * 1024 * 1024;

    public static void ConfigureHoldings(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = HoldingsTimeout;
        client.MaxResponseContentBufferSize = MaxHoldingsResponseBytes;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Upper bound for one chart request (the spec: 15 seconds).</summary>
    public static readonly TimeSpan ChartTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Ninety days of 1-minute bars are about 2 MB of JSON; anything beyond 16 MB is refused.</summary>
    public const int MaxChartResponseBytes = 16 * 1024 * 1024;

    public static void ConfigureChart(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = ChartTimeout;
        client.MaxResponseContentBufferSize = MaxChartResponseBytes;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Upper bound for one quote request (15 seconds).</summary>
    public static readonly TimeSpan QuoteTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A quote answer for 1,000 securities is a few hundred kilobytes with the order book; anything beyond 4 MB is refused.</summary>
    public const int MaxQuoteResponseBytes = 4 * 1024 * 1024;

    public static void ConfigureQuote(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = QuoteTimeout;
        client.MaxResponseContentBufferSize = MaxQuoteResponseBytes;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        // A redirect would leave the allow-list unchecked (it is followed inside this handler): never follow one.
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// The JSON body of a successful answer (a detached copy), or why there is none. With <paramref name="tokenCall"/> (a call that sends the
    /// owner's access token) a 401 or 403, or any 4xx that carries Dhan's DH-901, is <see cref="BrokerFailureKind.Unauthorized"/>: the token is expired or revoked.
    /// </summary>
    public static async ValueTask<BrokerResponse<JsonElement>> SendAsync(
        HttpClient client, HttpRequestMessage request, string operation, ILogger logger, CancellationToken cancellationToken, bool tokenCall = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var status = (int)response.StatusCode;
            var body = await ReadJsonAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (body is { } json)
                {
                    return BrokerResponse.Ok(json);
                }

                LogFailed(logger, operation, status, "unreadable answer", null);
                return BrokerResponse.Fail<JsonElement>(BrokerFailureKind.Unavailable);
            }

            // A 429 is Dhan's rate limit whether or not the body says so (DH-904): the quiet retries of ADR 0027 tell it from a refusal by the code.
            var code = (body is { } error ? ErrorCode(error) : null) ?? (status == 429 ? RateLimitCode : null);
            var kind = status switch
            {
                // Dhan also reports an invalid or expired token as DH-901 with other 4xx statuses (400 has been seen).
                401 or 403 when tokenCall => BrokerFailureKind.Unauthorized,
                >= 400 and < 500 when tokenCall && code == TokenRefusedCode => BrokerFailureKind.Unauthorized,
                429 => BrokerFailureKind.RateLimited,
                >= 400 and < 500 when code == RateLimitCode => BrokerFailureKind.RateLimited,
                >= 400 and < 500 => BrokerFailureKind.Rejected,
                _ => BrokerFailureKind.Unavailable,
            };
            var outcome = kind == BrokerFailureKind.Unavailable ? "failed" : "refused";
            LogFailed(logger, operation, status, outcome, code);
            return BrokerResponse.Fail<JsonElement>(kind, code);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, operation, null, "timed out", null);
            return BrokerResponse.Fail<JsonElement>(BrokerFailureKind.Unavailable);
        }
        catch (HttpRequestException ex)
        {
            // The exception text is not logged: it can name the request URL. The error kind says enough.
            var outcome = "network failure: " + ex.HttpRequestError;
            LogFailed(logger, operation, null, outcome, null);
            return BrokerResponse.Fail<JsonElement>(BrokerFailureKind.Unavailable);
        }
    }

    /// <summary>The first property of the names that holds a string or number, as text; null when none does or it is blank.</summary>
    public static string? Text(JsonElement element, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Dhan's own error code from an error body (<c>errorCode</c>), only when it has Dhan's documented form (<c>DH-901</c>): anything
    /// else could be an echo of what was sent. Message texts are never taken.
    /// </summary>
    public static string? ErrorCode(JsonElement body)
    {
        var code = Text(body, "errorCode", "error_code");
        return code is not null && DhanCode().IsMatch(code) ? code : null;
    }

    /// <summary>A failure for an answer that lacks what was expected: refused when it carries a broker error code, otherwise unavailable.</summary>
    public static BrokerResponse<T> Unexpected<T>(JsonElement body, string operation, ILogger logger)
    {
        var code = ErrorCode(body);
        LogFailed(logger, operation, null, "answer without the expected fields", code);
        var kind = code switch
        {
            null => BrokerFailureKind.Unavailable,
            RateLimitCode => BrokerFailureKind.RateLimited,
            _ => BrokerFailureKind.Rejected,
        };
        return BrokerResponse.Fail<T>(kind, code);
    }

    private static async ValueTask<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, default, cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^DH-[0-9]{3,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex DhanCode();

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Dhan {Operation} failed: {Outcome} (HTTP {Status}, broker error code {BrokerCode})")]
    internal static partial void LogFailed(ILogger logger, string operation, int? status, string outcome, string? brokerCode);
}
