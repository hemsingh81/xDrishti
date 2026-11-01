using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Dhan's expired-options bars for the backfill (P14-03, ADR 0177): <c>POST /v2/charts/rollingoption</c> with <c>{"exchangeSegment":"NSE_FNO","interval":"5","securityId":13,"instrument":"OPTIDX","expiryFlag":"WEEK","expiryCode":1,"strike":"ATM+10",
/// "drvOptionType":"CALL","requiredData":[...],"fromDate":"2026-09-01","toDate":"2026-10-01"}</c>; headers <c>access-token</c> and <c>client-id</c> of the primary Data account (from <see cref="IDataAccountCredentials"/>). Read-only despite the POST; the guard
/// allows nothing else and no query. Every request waits on the one <see cref="BackfillThrottle"/>. Nothing is retried, and neither the token, the client id, the URL, the body nor any text of Dhan's answer is logged or put into a reply: a reply is an outcome
/// word, the HTTP status, a well-formed Dhan code and - for a success - the bytes for the job to read. <b>Not verified against the real API</b> (the body shape, the value of <c>interval</c> as text, <c>expiryCode</c>): the owner's probe is the check.
/// </summary>
internal sealed partial class DhanRollingOptionClient(IHttpClientFactory httpClients, IDataAccountCredentials credentials, BackfillThrottle throttle, OptionBackfillSettings settings, TimeProvider clock) : IOptionBackfillClient
{
    private static readonly string[] Required = ["open", "high", "low", "close", "volume", "strike", "oi", "iv", "spot"];

    public async ValueTask<BackfillReply> SendAsync(RollingRequest request, TimeSpan spacing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await credentials.GetCredentialsAsync(cancellationToken) is not { } account)
        {
            return new BackfillReply(BackfillOutcomes.TokenRefused, null, null, null, 0, NoCredentials: true);
        }

        await throttle.WaitAsync(spacing, cancellationToken);
        var started = clock.GetTimestamp();
        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanRollingOption);
        using var content = new ByteArrayContent(Body(request));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, BrokerEndpoints.DhanRollingOption) { Content = content };
        message.Headers.TryAddWithoutValidation("access-token", account.AccessToken.Reveal());
        message.Headers.TryAddWithoutValidation("client-id", account.ClientId);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.Timeout);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            var code = CodeOf(body);
            var outcome = BackfillOutcomeMap.From(status, code);
            return new BackfillReply(outcome, status, code, outcome == BackfillOutcomes.Ok ? body : null, Elapsed(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BackfillReply(BackfillOutcomes.Timeout, null, null, null, Elapsed(started));
        }
        catch (HttpRequestException)
        {
            // The exception text is not kept: it can name the request URL. A network failure and an answer that is too long are both a server-side gap.
            return new BackfillReply(BackfillOutcomes.ServerError, null, null, null, Elapsed(started));
        }
    }

    /// <summary>The JSON body (UNVERIFIED shape, see the class summary). Dates are <c>yyyy-MM-dd</c>; <c>toDate</c> is not inclusive.</summary>
    internal static byte[] Body(RollingRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) // the plus of ATM+10 is written as it is, not as \u002B
        {
            writer.WriteStartObject();
            writer.WriteString("exchangeSegment", "NSE_FNO");
            writer.WriteString("interval", request.IntervalMinutes.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("securityId", request.SecurityId);
            writer.WriteString("instrument", request.Instrument);
            writer.WriteString("expiryFlag", request.ExpiryFlag == 'W' ? "WEEK" : "MONTH");
            writer.WriteNumber("expiryCode", request.ExpiryCode);
            writer.WriteString("strike", Strike(request.Offset));
            writer.WriteString("drvOptionType", request.Side == "CE" ? "CALL" : "PUT");
            writer.WriteStartArray("requiredData");
            foreach (var field in Required)
            {
                writer.WriteStringValue(field);
            }

            writer.WriteEndArray();
            writer.WriteString("fromDate", request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("toDate", request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary><c>ATM</c>, <c>ATM+3</c>, <c>ATM-3</c>.</summary>
    internal static string Strike(int offset) => offset == 0 ? "ATM" : offset > 0 ? $"ATM+{offset.ToString(CultureInfo.InvariantCulture)}" : $"ATM{offset.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Dhan's own error code (<c>DH-905</c> form, or the 806 to 809 socket-style numbers) from the answer, and nothing else of it; null when there is none.</summary>
    internal static string? CodeOf(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var code = BrokerHttp.Text(root, "errorCode", "error_code");
            if (code is null && root.TryGetProperty("remarks", out var remarks) && remarks.ValueKind == JsonValueKind.Object)
            {
                code = BrokerHttp.Text(remarks, "errorCode", "error_code");
            }

            return code is not null && Code().IsMatch(code) ? code : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private int Elapsed(long started) => (int)Math.Min(int.MaxValue, clock.GetElapsedTime(started).TotalMilliseconds);

    [GeneratedRegex("^(DH-[0-9]{3,4}|80[0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
