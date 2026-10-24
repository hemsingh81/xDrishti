using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Dhan's market quote for securities of the NSE equity segment (<c>POST /v2/marketfeed/quote</c>, ADR 0019 §1): read-only despite the POST. Body (JSON) <c>{"NSE_EQ":[securityId, …]}</c> with at most
/// <see cref="MaxPerRequest"/> ids (more are split into several requests, each waiting for the <see cref="QuoteThrottle"/>, at most one per second); headers <c>access-token</c> and <c>client-id</c> of the primary
/// Data account (from <see cref="IDataAccountCredentials"/>), never logged. The answer is <c>{"data":{"NSE_EQ":{"&lt;id&gt;":{"last_price":…,"ohlc":{"close":…},…}}},"status":"success"}</c>: the last traded price and
/// <c>ohlc.close</c>, the previous session's close. A security that is missing from the answer or whose <c>last_price</c> is not a positive number is simply left out; a <c>close</c> that is not a positive number
/// is read as no previous close. <b>Not verified against the real API</b> (headers, field names, the meaning of <c>close</c>, the rate): the first real run is the owner's check, so everything unexpected fails the whole call
/// with a safe fixed message instead of being guessed at (<see cref="QuoteFailureKind.Failed"/>).
/// <para>
/// 401 and 403 (or a 4xx carrying Dhan's DH-901 or DH-902) are <see cref="QuoteFailureKind.Unauthorized"/>; 429 (or DH-904) is <see cref="QuoteFailureKind.RateLimited"/>; any other status, a timeout, a network
/// failure, an answer that is not JSON or not of the expected shape is <see cref="QuoteFailureKind.Failed"/>. Nothing is retried here (the next tick is the retry), and neither the token, the client id, the URL nor
/// any part of Dhan's answer is logged or put into a message (only the HTTP status and a well-formed Dhan error code).
/// </para>
/// </summary>
internal sealed class DhanQuoteClient(IHttpClientFactory httpClients, IDataAccountCredentials credentials, QuoteThrottle throttle, ILogger<DhanQuoteClient> logger) : IQuoteSource
{
    /// <summary>Dhan's limit of securities per quote request.</summary>
    public const int MaxPerRequest = 1000;

    public const string NoToken = "The Data account has no access token that works.";

    public const string TokenRefused = "The Data account's token was refused by Dhan.";

    public const string RateLimitReached = "Dhan's rate limit was reached; the next tick tries again.";

    public const string Unexpected = "Dhan's answer was not what was expected.";

    public const string TimedOut = "Dhan did not answer in time.";

    public const string Segment = "NSE_EQ";

    private const string Operation = "quote";

    /// <summary>Numbers beyond this are not prices: the quote is left out (a price that would not fit the column).</summary>
    private const decimal Sane = 1_000_000_000m;

    public async ValueTask<QuoteResult> QuoteAsync(IReadOnlyCollection<string> securityIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityIds);
        var ids = new List<long>();
        var seen = new HashSet<long>();
        foreach (var text in securityIds)
        {
            // Dhan's security ids are numbers; anything else cannot be asked for (and is never put into a request body).
            if (long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return new QuoteResult([]);
        }

        if (await credentials.GetCredentialsAsync(cancellationToken) is not { } account)
        {
            return QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Unauthorized, NoToken), 0);
        }

        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanQuote);
        var quotes = new List<SecurityQuote>();
        var requests = 0;
        foreach (var chunk in ids.Chunk(MaxPerRequest))
        {
            await throttle.WaitAsync(cancellationToken);
            requests++;
            var attempt = await SendOnceAsync(client, account, chunk, cancellationToken);
            if (attempt.Failure is { } failure)
            {
                // a request that failed leaves no half answer: nothing of the earlier ones is used either
                return QuoteResult.Failed(failure, requests);
            }

            quotes.AddRange(attempt.Quotes!);
        }

        return new QuoteResult(quotes, null, requests);
    }

    /// <summary>The JSON body for the ids: <c>{"NSE_EQ":[1333,11536]}</c>.</summary>
    internal static byte[] Body(IEnumerable<long> ids)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray(Segment);
            foreach (var id in ids)
            {
                writer.WriteNumberValue(id);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The body as JSON; null when it is not JSON or longer than <see cref="BrokerHttp.MaxQuoteResponseBytes"/> (read with a limit of our own, so that too long is told apart from a network failure).</summary>
    private static async ValueTask<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > BrokerHttp.MaxQuoteResponseBytes)
        {
            return null;
        }

        try
        {
            await using var stream = new LimitedStream(await response.Content.ReadAsStreamAsync(cancellationToken), BrokerHttp.MaxQuoteResponseBytes);
            return await JsonDocument.ParseAsync(stream, default, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TooLongException)
        {
            return null;
        }
    }

    /// <summary>Dhan's own error code (<c>DH-905</c> form only), at the top of the answer or under <c>remarks</c>.</summary>
    private static string? CodeOf(JsonElement root) =>
        BrokerHttp.ErrorCode(root) ?? (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("remarks", out var remarks) ? BrokerHttp.ErrorCode(remarks) : null);

    private static Attempt Fail(QuoteFailureKind kind, string message) => new(null, new QuoteFailure(kind, message));

    private async ValueTask<Attempt> SendOnceAsync(HttpClient client, DataAccountCredentials account, long[] ids, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(Body(ids));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, BrokerEndpoints.DhanMarketQuote) { Content = content };
        message.Headers.TryAddWithoutValidation("access-token", account.AccessToken.Reveal());
        message.Headers.TryAddWithoutValidation("client-id", account.ClientId);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrokerHttp.QuoteTimeout); // the client's own timeout ends with the headers; the body is read with this one
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            using var document = await ReadAsync(response, timeout.Token);
            var root = document?.RootElement;
            var code = root is { } element ? CodeOf(element) : null;
            if (response.IsSuccessStatusCode && code is null)
            {
                if (root is { } answer)
                {
                    return Parse(answer, ids);
                }

                BrokerHttp.LogFailed(logger, Operation, status, "unreadable answer", null);
                return Fail(QuoteFailureKind.Failed, Unexpected);
            }

            return Refusal(status, code);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            BrokerHttp.LogFailed(logger, Operation, null, "timed out", null);
            return Fail(QuoteFailureKind.Failed, TimedOut);
        }
        catch (HttpRequestException ex)
        {
            // The exception text is not logged: it can name the request URL. The error kind says enough.
            BrokerHttp.LogFailed(logger, Operation, null, "network failure: " + ex.HttpRequestError, null);
            return Fail(QuoteFailureKind.Failed, $"Dhan could not be reached ({ex.HttpRequestError}).");
        }
    }

    private Attempt Refusal(int status, string? code)
    {
        if (status is 401 or 403 || (status < 500 && code is "DH-901" or "DH-902"))
        {
            BrokerHttp.LogFailed(logger, Operation, status, "refused (token or subscription)", code);
            return Fail(QuoteFailureKind.Unauthorized, TokenRefused);
        }

        if (status == 429 || code == "DH-904")
        {
            BrokerHttp.LogFailed(logger, Operation, status, "rate limited", code);
            return Fail(QuoteFailureKind.RateLimited, RateLimitReached);
        }

        BrokerHttp.LogFailed(logger, Operation, status, "failed", code);
        var detail = code is null ? $"HTTP {status}" : $"HTTP {status}, {code}";
        return Fail(QuoteFailureKind.Failed, status is >= 300 and < 400 ? $"Dhan answered with a redirect ({detail}), which is not followed." : $"Dhan's quote service answered {detail}.");
    }

    private Attempt Parse(JsonElement root, long[] ids)
    {
        if (root.ValueKind != JsonValueKind.Object || IsFailed(root) || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            BrokerHttp.LogFailed(logger, Operation, null, "answer without the expected fields", null);
            return Fail(QuoteFailureKind.Failed, Unexpected);
        }

        var quotes = new List<SecurityQuote>();
        if (!data.TryGetProperty(Segment, out var segment))
        {
            return new Attempt(quotes, null);
        }

        if (segment.ValueKind != JsonValueKind.Object)
        {
            BrokerHttp.LogFailed(logger, Operation, null, "answer without the expected fields", null);
            return Fail(QuoteFailureKind.Failed, Unexpected);
        }

        var wanted = ids.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in segment.EnumerateObject())
        {
            if (wanted.Remove(entry.Name) && Price(entry.Value, "last_price") is { } last)
            {
                quotes.Add(new SecurityQuote(entry.Name, last, entry.Value.TryGetProperty("ohlc", out var ohlc) ? Price(ohlc, "close") : null, TradeTime(entry.Value), Volume(entry.Value)));
            }
        }

        return new Attempt(quotes, null);
    }

    /// <summary>The answer says it failed (<c>"status":"failed"</c> or <c>"failure"</c>) although the HTTP status was a success.</summary>
    private static bool IsFailed(JsonElement root) =>
        root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
        && status.GetString() is { } text && (text.Equals("failed", StringComparison.OrdinalIgnoreCase) || text.Equals("failure", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>last_trade_time</c> of the same answer (P7-11): India time, read from <c>dd/MM/yyyy HH:mm:ss</c> or from epoch seconds (no other form is guessed at); null when missing or in none of those forms. The format is not verified against the real
    /// API: an unreadable time is null, and the pre-open check then treats the price as unavailable (fail closed).
    /// </summary>
    internal static DateTimeOffset? TradeTime(JsonElement owner)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty("last_trade_time", out var value))
        {
            return null;
        }

        var ist = System.Globalization.CultureInfo.InvariantCulture;
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text)
        {
            return DateTime.TryParseExact(text, "dd/MM/yyyy HH:mm:ss", ist, System.Globalization.DateTimeStyles.None, out var local)
                ? new DateTimeOffset(local, XDrishti.Domain.Market.IndiaTime.Offset)
                : null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds > 0 && seconds < 32_503_680_000L ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    /// <summary>The traded volume of the same answer; null when missing, not a whole number or negative.</summary>
    private static long? Volume(JsonElement owner) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty("volume", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number : null;

    /// <summary>A positive number under the name; null when it is missing, not a number (a text is no number here), zero, negative or absurd.</summary>
    private static decimal? Price(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number > 0m && number < Sane
            ? number
            : null;

    private readonly record struct Attempt(IReadOnlyList<SecurityQuote>? Quotes, QuoteFailure? Failure);

    private sealed class TooLongException : IOException
    {
    }

    /// <summary>Passes the bytes of the answer on and fails as soon as there are more than the limit.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            _read += count;
            return _read > limit ? throw new TooLongException() : count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
            // nothing is written
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
