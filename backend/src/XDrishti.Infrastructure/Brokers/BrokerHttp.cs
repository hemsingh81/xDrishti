using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Sends one request to Dhan and turns the answer into JSON or a <see cref="BrokerFailure"/>: 2xx → the JSON body; 4xx → rejected;
/// anything else (3xx — redirects are not followed —, 5xx, timeout, network failure, unreadable body) → unavailable. Never retries: codes are
/// single-use. Only the operation, the HTTP status and Dhan's error code are logged — never URLs, headers or bodies.
/// </summary>
internal static partial class BrokerHttp
{
    /// <summary>Upper bound for one broker call (the spec: 15 seconds).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Broker answers are small; anything bigger is refused.</summary>
    public const int MaxResponseBytes = 256 * 1024;

    public static void Configure(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = Timeout;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        // A redirect would leave the allow-list unchecked (it is followed inside this handler): never follow one.
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>The JSON body of a successful answer (a detached copy), or why there is none.</summary>
    public static async ValueTask<BrokerResponse<JsonElement>> SendAsync(
        HttpClient client, HttpRequestMessage request, string operation, ILogger logger, CancellationToken cancellationToken)
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

            var code = body is { } error ? ErrorCode(error) : null;
            var kind = status is >= 400 and < 500 ? BrokerFailureKind.Rejected : BrokerFailureKind.Unavailable;
            var outcome = kind == BrokerFailureKind.Rejected ? "refused" : "failed";
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
        return BrokerResponse.Fail<T>(code is null ? BrokerFailureKind.Unavailable : BrokerFailureKind.Rejected, code);
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
    private static partial void LogFailed(ILogger logger, string operation, int? status, string outcome, string? brokerCode);
}
