using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.OptionChain;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Dhan's option chain and expiry list for the option-chain recorder (P14-02, ADR 0176): <c>POST /v2/optionchain</c> with <c>{"UnderlyingScrip":13,"UnderlyingSeg":"IDX_I","Expiry":"2026-11-03"}</c> and <c>POST /v2/optionchain/expirylist</c> with
/// the same body without <c>Expiry</c>; headers <c>access-token</c> and <c>client-id</c> of the primary Data account (from <see cref="IDataAccountCredentials"/>). Read-only despite the POST; the guard allows nothing else and no query. Every request waits on the
/// one <see cref="ChainThrottle"/> (at least 3 seconds after the last one, of either kind). Nothing is retried, and neither the token, the client id, the URL, the body nor any text of Dhan's answer is logged or put into a reply: a reply is an outcome word,
/// the HTTP status, a well-formed Dhan code and - for a success - the bytes for the recorder to read. <b>Not verified against the real API</b>: the pilot is the owner's check.
/// </summary>
internal sealed partial class DhanOptionChainClient(IHttpClientFactory httpClients, IDataAccountCredentials credentials, ChainThrottle throttle, OptionChainSettings settings, TimeProvider clock) : IOptionChainClient
{
    public async ValueTask<ChainReply> SendAsync(ChainRequest request, TimeSpan spacing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await credentials.GetCredentialsAsync(cancellationToken) is not { } account)
        {
            return new ChainReply(ChainOutcomes.TokenRefused, null, null, null, 0, NoCredentials: true);
        }

        await throttle.WaitAsync(spacing, cancellationToken);
        var started = clock.GetTimestamp();
        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanOptionChain);
        using var content = new ByteArrayContent(Body(request));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Kind == ChainKind.Chain ? BrokerEndpoints.DhanOptionChain : BrokerEndpoints.DhanOptionChainExpiryList) { Content = content };
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
            var outcome = ChainOutcomeMap.From(status, code);
            return new ChainReply(outcome, status, code, outcome == ChainOutcomes.Ok ? body : null, Elapsed(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ChainReply(ChainOutcomes.Timeout, null, null, null, Elapsed(started));
        }
        catch (HttpRequestException)
        {
            // The exception text is not kept: it can name the request URL. A network failure and an answer that is too long are both a server-side gap.
            return new ChainReply(ChainOutcomes.ServerError, null, null, null, Elapsed(started));
        }
    }

    /// <summary>The JSON body: <c>{"UnderlyingScrip":13,"UnderlyingSeg":"IDX_I","Expiry":"2026-11-03"}</c>; the expiry list has no <c>Expiry</c>.</summary>
    internal static byte[] Body(ChainRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("UnderlyingScrip", request.UnderlyingScrip);
            writer.WriteString("UnderlyingSeg", request.Segment);
            if (request.Kind == ChainKind.Chain && request.Expiry is { } expiry)
            {
                writer.WriteString("Expiry", expiry.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

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
