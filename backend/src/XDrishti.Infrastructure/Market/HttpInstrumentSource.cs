using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Market;

/// <summary>
/// Downloads the two public master files and NSE's public trading-holiday list (also the <see cref="IHolidaySource"/>: the same allow-list, caps and rules). GET only,
/// to the allow-listed URLs (<see cref="MasterDataEndpoints"/>), no credentials or cookies, no redirects, size and time caps. Only the source's name, the HTTP status and
/// a short outcome are logged — never URLs or content.
/// </summary>
internal sealed partial class HttpInstrumentSource(IHttpClientFactory clients, TimeProvider clock, ILogger<HttpInstrumentSource> logger) : IInstrumentSource, IHolidaySource
{
    private const string DhanName = "Dhan's instrument file";

    private const string NseName = "NSE's sector list";

    private const string HolidayName = "NSE's holiday list";

    public ValueTask<SourceDownload> OpenMasterAsync(SourceStamp? known, CancellationToken cancellationToken) =>
        OpenAsync(MasterDataHttpClientNames.Dhan, MasterDataEndpoints.DhanScripMaster, known, MasterDataHttp.DhanMaxBytes, MasterDataHttp.DhanTimeout, DhanName, cancellationToken);

    public ValueTask<SourceDownload> OpenSectorsAsync(CancellationToken cancellationToken) =>
        OpenAsync(MasterDataHttpClientNames.Nse, MasterDataEndpoints.NseTotalMarketList, null, MasterDataHttp.NseMaxBytes, MasterDataHttp.NseTimeout, NseName, cancellationToken);

    public ValueTask<SourceDownload> OpenHolidaysAsync(CancellationToken cancellationToken) =>
        OpenAsync(MasterDataHttpClientNames.NseApi, MasterDataEndpoints.NseHolidayMaster, null, MasterDataHttp.HolidayMaxBytes, MasterDataHttp.HolidayTimeout, HolidayName, cancellationToken);

    private async ValueTask<SourceDownload> OpenAsync(
        string clientName, Uri url, SourceStamp? known, long maxBytes, TimeSpan timeout, string what, CancellationToken cancellationToken)
    {
        var deadline = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        HttpResponseMessage? response = null;
        var handedOver = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddValidators(request, known);
            response = await clients.CreateClient(clientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            var status = response.StatusCode;
            var stamp = new SourceStamp(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified?.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (status == HttpStatusCode.NotModified && known is { IsEmpty: false })
            {
                LogAnswered(logger, what, (int)status);
                return Done(SourceDownload.NotModified(stamp), response, deadline);
            }

            if (status != HttpStatusCode.OK)
            {
                LogAnswered(logger, what, (int)status);
                return Done(SourceDownload.Failed($"{what} answered HTTP {(int)status}."), response, deadline);
            }

            if (response.Content.Headers.ContentLength > maxBytes)
            {
                return Done(SourceDownload.Failed($"{what} is larger than the allowed {maxBytes / (1024 * 1024)} MB."), response, deadline);
            }

            var body = await response.Content.ReadAsStreamAsync(linked.Token);
            LogAnswered(logger, what, (int)status);
            var owner = new Owner(response, deadline);
            handedOver = true;
            return SourceDownload.WithContent(new GuardedStream(body, maxBytes, what, deadline.Token), stamp, owner);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, what, "timed out");
            return Done(SourceDownload.Failed($"{what} did not answer in time."), response, deadline);
        }
        catch (HttpRequestException ex)
        {
            // The exception text is not logged or shown: it can name the request URL.
            LogFailed(logger, what, "network failure: " + ex.HttpRequestError);
            return Done(SourceDownload.Failed($"{what} could not be reached."), response, deadline);
        }
        finally
        {
            if (!handedOver)
            {
                // Every other way out (including exceptions such as the allow-list guard's) releases the response and the deadline timer.
                response?.Dispose();
                deadline.Dispose();
            }
        }
    }

    private static SourceDownload Done(SourceDownload result, HttpResponseMessage? response, CancellationTokenSource deadline)
    {
        response?.Dispose();
        deadline.Dispose();
        return result;
    }

    private static void AddValidators(HttpRequestMessage request, SourceStamp? known)
    {
        if (known is null || known.IsEmpty)
        {
            return;
        }

        if (!string.IsNullOrEmpty(known.ETag) && EntityTagHeaderValue.TryParse(known.ETag, out var etag))
        {
            request.Headers.IfNoneMatch.Add(etag);
        }

        if (!string.IsNullOrEmpty(known.LastModified) && DateTimeOffset.TryParse(known.LastModified, System.Globalization.CultureInfo.InvariantCulture, out var modified))
        {
            request.Headers.IfModifiedSince = modified;
        }
    }

    private sealed class Owner(HttpResponseMessage response, CancellationTokenSource deadline) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            response.Dispose();
            deadline.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "{Source} answered HTTP {Status}")]
    private static partial void LogAnswered(ILogger logger, string source, int status);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning, Message = "{Source} could not be fetched: {Outcome}")]
    private static partial void LogFailed(ILogger logger, string source, string outcome);
}
