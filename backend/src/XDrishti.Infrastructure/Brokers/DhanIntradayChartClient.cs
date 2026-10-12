using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// One request to Dhan's Data API for the 1-minute bars of one instrument and one window of at most 90 days (<c>POST /v2/charts/intraday</c>, ADR 0016 §7).
/// Body (JSON): <c>securityId</c> (text), <c>exchangeSegment</c> (<c>NSE_EQ</c> for shares and ETFs, <c>IDX_I</c> for indices), <c>instrument</c> (<c>EQUITY</c> or
/// <c>INDEX</c>), <c>interval</c> 1, <c>oi</c> false, <c>fromDate</c> and <c>toDate</c> as <c>yyyy-MM-dd HH:mm:ss</c> in India time; headers <c>access-token</c> and
/// <c>Content-Type: application/json</c> (no client id: the documentation lists none and the holdings read works without it).
/// The answer is parallel arrays <c>open, high, low, close, volume, timestamp</c> (and <c>open_interest</c>, unused); a <c>timestamp</c> is Unix time in seconds, or in
/// milliseconds when it has 13 digits (the documentation does not say; <see cref="IntradayAlignmentCheck"/> proves the reading from the data).
/// <para>
/// Every request first waits for the shared <see cref="RequestThrottle"/>. A 401/403, or a 4xx that carries Dhan's DH-901 or DH-902 (token or Data API subscription refused),
/// is <see cref="IntradayFailureKind.Unauthorized"/>. A 429, a 5xx or DH-904 (rate limit) is tried <b>once more</b> after <see cref="RetryBackoff"/>; any other 4xx is never
/// retried. Anything else that goes wrong is a <see cref="IntradayFailureKind.Failed"/> with a safe message: the token, the URL and the text of Dhan's answer are never
/// logged or put into a message (only the HTTP status and a well-formed Dhan error code).
/// </para>
/// </summary>
internal sealed class DhanIntradayChartClient(IHttpClientFactory httpClients, RequestThrottle throttle, TimeProvider clock, ILogger<DhanIntradayChartClient> logger)
{
    /// <summary>The wait before the one retry of a request Dhan answered with 429 or 5xx.</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(2);

    /// <summary>More bars than 90 days can hold (a day has 1,440 minutes); a bigger answer is unusable.</summary>
    internal const int MaxBars = 150_000;

    /// <summary>Timestamps from here on are milliseconds (13 digits); seconds stay below it until the year 5138.</summary>
    private const double MillisecondsFrom = 100_000_000_000d;

    private const string Operation = "intraday chart";

    private const string NsePlace = "NSE_EQ";

    private const string IndexPlace = "IDX_I";

    private static readonly string[] Arrays = ["open", "high", "low", "close", "volume", "timestamp"];

    public async ValueTask<ChartWindowResult> FetchAsync(SecretValue accessToken, IntradayTarget target, FetchWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(target);
        var period = Describe(window);
        if (!TryBody(target, window, out var body))
        {
            return Fail(0, IntradayFailureKind.Failed, $"{period}: the instrument has no usable Dhan segment ('{Clean(target.Segment)}') for its type");
        }

        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanChart);
        var requests = 0;
        for (var attempt = 0; ; attempt++)
        {
            await throttle.WaitAsync(cancellationToken);
            requests++;
            var outcome = await SendOnceAsync(client, accessToken, body, period, cancellationToken);
            if (outcome.Retry && attempt == 0)
            {
                await Task.Delay(RetryBackoff, clock, cancellationToken);
                continue;
            }

            return outcome.Failure is { } failure ? new ChartWindowResult([], requests, failure) : new ChartWindowResult(outcome.Bars!, requests, null);
        }
    }

    /// <summary>The JSON body for the target and window; false when the target's type and segment do not belong together (nothing is guessed).</summary>
    internal static bool TryBody(IntradayTarget target, FetchWindow window, out byte[] body)
    {
        body = [];
        string? instrument = (target.Type, target.Segment) switch
        {
            ("share" or "etf", NsePlace) => "EQUITY",
            ("index", IndexPlace) => "INDEX",
            _ => null,
        };
        if (instrument is null || string.IsNullOrWhiteSpace(target.SecurityId))
        {
            return false;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("securityId", target.SecurityId);
            writer.WriteString("exchangeSegment", target.Segment);
            writer.WriteString("instrument", instrument);
            writer.WriteNumber("interval", 1);
            writer.WriteBoolean("oi", false);
            writer.WriteString("fromDate", Stamp(window.From));
            writer.WriteString("toDate", Stamp(window.To));
            writer.WriteEndObject();
        }

        body = stream.ToArray();
        return true;
    }

    /// <summary>Dhan's date format, in India time.</summary>
    internal static string Stamp(DateTimeOffset instant) => IndiaTime.ToIst(instant).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Describe(FetchWindow window) =>
        string.Create(CultureInfo.InvariantCulture, $"window {IndiaTime.ToIst(window.From):yyyy-MM-dd} to {IndiaTime.ToIst(window.To):yyyy-MM-dd}");

    /// <summary>A segment text safe to echo: letters, digits and underscores only, short.</summary>
    private static string Clean(string? text) =>
        new([.. (text ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(16)]);

    private static ChartWindowResult Fail(int requests, IntradayFailureKind kind, string message) =>
        new([], requests, new IntradayFailure(kind, message));

    private async ValueTask<Attempt> SendOnceAsync(HttpClient client, SecretValue accessToken, byte[] body, string period, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, BrokerEndpoints.DhanIntradayChart) { Content = content };
        message.Headers.TryAddWithoutValidation("access-token", accessToken.Reveal());
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var status = (int)response.StatusCode;
            using var document = await ReadAsync(response, cancellationToken);
            var root = document?.RootElement;
            var code = root is { } element ? BrokerHttp.ErrorCode(element) : null;
            if (response.IsSuccessStatusCode && code is null)
            {
                return root is { } answer ? Parse(answer, period) : Failed($"{period}: Dhan's answer could not be read");
            }

            return Refusal(status, code, period);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            BrokerHttp.LogFailed(logger, Operation, null, "timed out", null);
            return Failed($"{period}: Dhan did not answer in time");
        }
        catch (HttpRequestException ex)
        {
            // The exception text is not logged: it can name the request URL. The error kind says enough.
            BrokerHttp.LogFailed(logger, Operation, null, "network failure: " + ex.HttpRequestError, null);
            return Failed($"{period}: Dhan could not be reached ({ex.HttpRequestError})");
        }
    }

    private Attempt Refusal(int status, string? code, string period)
    {
        var unauthorized = status is 401 or 403 || (status < 500 && code is "DH-901" or "DH-902");
        var retry = !unauthorized && (status is 429 or >= 500 || code == "DH-904");
        var outcome = unauthorized ? "refused (token or subscription)" : retry ? "busy or failed" : "refused";
        BrokerHttp.LogFailed(logger, Operation, status, outcome, code);
        if (unauthorized)
        {
            return new Attempt(null, new IntradayFailure(IntradayFailureKind.Unauthorized, "Dhan refused the access token or the Data API subscription"), false);
        }

        var detail = code is null ? $"HTTP {status}" : $"HTTP {status}, {code}";
        var message = status is >= 300 and < 400
            ? $"{period}: Dhan answered with a redirect (HTTP {status}), which is not followed"
            : $"{period}: Dhan answered {detail}";
        return new Attempt(null, new IntradayFailure(IntradayFailureKind.Failed, message), retry);
    }

    private static Attempt Failed(string message) => new(null, new IntradayFailure(IntradayFailureKind.Failed, message), false);

    private static async ValueTask<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, default, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Attempt Parse(JsonElement root, string period)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Failed($"{period}: Dhan's answer was not what was expected");
        }

        var columns = new JsonElement[Arrays.Length];
        for (var i = 0; i < Arrays.Length; i++)
        {
            if (!root.TryGetProperty(Arrays[i], out columns[i]) || columns[i].ValueKind != JsonValueKind.Array)
            {
                return Failed($"{period}: Dhan's answer lacks the '{Arrays[i]}' array");
            }
        }

        var count = columns[0].GetArrayLength();
        if (columns.Any(c => c.GetArrayLength() != count))
        {
            return Failed($"{period}: Dhan's arrays have different lengths");
        }

        if (count > MaxBars)
        {
            return Failed($"{period}: Dhan's answer has {count} bars, more than a window can hold");
        }

        var bars = new Bar[count];
        var cursors = columns.Select(c => c.EnumerateArray()).ToArray();
        var values = new double[Arrays.Length];
        for (var row = 0; row < count; row++)
        {
            for (var i = 0; i < cursors.Length; i++)
            {
                if (!cursors[i].MoveNext() || cursors[i].Current.ValueKind != JsonValueKind.Number || !cursors[i].Current.TryGetDouble(out values[i]) || !double.IsFinite(values[i]))
                {
                    return Failed($"{period}: Dhan's '{Arrays[i]}' array holds a value that is not a number");
                }
            }

            var seconds = values[5] >= MillisecondsFrom ? values[5] / 1000d : values[5];
            if (Math.Abs(seconds) > 1e11 || Math.Abs(values[4]) > 1e15)
            {
                return Failed($"{period}: Dhan's answer holds a time or volume out of range");
            }

            bars[row] = new Bar((long)Math.Floor(seconds), values[0], values[1], values[2], values[3], (long)Math.Round(values[4]));
        }

        return new Attempt(bars, null, false);
    }

    private readonly record struct Attempt(IReadOnlyList<Bar>? Bars, IntradayFailure? Failure, bool Retry);
}
