using System.Net;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Baskets;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>
/// The conditional GET of one catalogue file through the guarded client (the url is catalogue data, never user input): <c>If-None-Match</c> / <c>If-Modified-Since</c> from the stored validators, no redirects, no cookies, size and time caps, the alternate host on
/// 404 or a server failure. A network problem is a status 0 answer, never an exception; only the host and path are logged, never content.
/// </summary>
internal sealed partial class NseBasketSourceFetcher(IHttpClientFactory clients, TimeProvider clock, ILogger<NseBasketSourceFetcher> logger) : IBasketSourceFetcher
{
    public const string ClientName = "basket-source";

    public const long MaxBytes = 5L * 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async ValueTask<SourceResponse> GetAsync(BasketSourceInfo source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var first = await GetOnceAsync(source.SourceUrl, source, cancellationToken);
        return first.Status is 0 or 404 or >= 500 && source.HostAlt is { Length: > 0 } alt ? (await GetOnceAsync(alt, source, cancellationToken)) with { Url = alt } : first with { Url = source.SourceUrl };
    }

    private async ValueTask<SourceResponse> GetOnceAsync(string url, BasketSourceInfo source, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            if (source.Etag is { Length: > 0 } etag)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (source.LastModified is { Length: > 0 } modified)
            {
                request.Headers.TryAddWithoutValidation("If-Modified-Since", modified);
            }

            using var response = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var status = (int)response.StatusCode;
            LogAnswered(logger, request.RequestUri!.Host, request.RequestUri.AbsolutePath, status);
            var etagOut = response.Headers.ETag?.ToString();
            var modifiedOut = response.Content.Headers.LastModified?.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new SourceResponse(status, null, null, etagOut, modifiedOut, null);
            }

            var contentType = response.Content.Headers.ContentType?.ToString();
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return new SourceResponse(status, contentType, null, etagOut, modifiedOut, "too_large");
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, linked.Token)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                {
                    return new SourceResponse(status, contentType, null, etagOut, modifiedOut, "too_large");
                }

                buffer.Write(chunk, 0, read);
            }

            return new SourceResponse(status, contentType, buffer.ToArray(), etagOut, modifiedOut, null);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return SourceResponse.Unreachable("timeout");
        }
        catch (MasterDataEndpointNotAllowedException)
        {
            return SourceResponse.Unreachable("not_allowed"); // a catalogue url outside the guard's list: refused before sending
        }
        catch (HttpRequestException ex)
        {
            return SourceResponse.Unreachable($"unreachable:{ex.HttpRequestError}"); // the exception text can name the url, so only the kind is kept
        }
    }

    [LoggerMessage(EventId = 3510, Level = LogLevel.Information, Message = "basket.fetch GET {Host}{Path} answered {Status}")]
    private static partial void LogAnswered(ILogger logger, string host, string path, int status);
}
