using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Market.Lists;

namespace XDrishti.Infrastructure.Market.Lists;

/// <summary>
/// GET of one dated NSE ban file through the guarded history client (DATA-01, ADR 0274): a path that is not on the allow-list is refused before sending (<see cref="ListFetchStatus.NotAllowed"/>). No cookies, no redirects, small size cap, 60 s limit.
/// Only host, path and status are logged. A 404 is "no such file"; anything else that is not 200 is a failure; nothing is worked around. This is the only caller of the archive path.
/// </summary>
internal sealed partial class NseListArchiveSource(IHttpClientFactory clients, TimeProvider clock, ILogger<NseListArchiveSource> logger) : IListArchiveSource
{
    public const int MaxBytes = 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async ValueTask<ListArchiveFetch> FetchBanAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var path = ListArchive.BanPath(date);
        using var deadline = new CancellationTokenSource(Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{ListArchive.Host}{path}"));
            using var response = await clients.CreateClient(MasterDataHttpClientNames.NseHistory).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            LogAnswered(logger, ListArchive.Host, path, (int)response.StatusCode);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ListArchiveFetch.Absent();
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return ListArchiveFetch.Failed($"NSE answered HTTP {(int)response.StatusCode} for {path}.");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return ListArchiveFetch.Failed($"The file {path} is larger than allowed.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16_384];
            int read;
            while ((read = await body.ReadAsync(chunk, linked.Token)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                {
                    return ListArchiveFetch.Failed($"The file {path} is larger than allowed.");
                }

                buffer.Write(chunk, 0, read);
            }

            return ListArchiveFetch.Ok(buffer.ToArray());
        }
        catch (MasterDataEndpointNotAllowedException ex)
        {
            return ListArchiveFetch.NotAllowed($"{ex.Message} (host {ListArchive.Host}, path {path})");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ListArchiveFetch.Failed($"NSE did not answer in time for {path}.");
        }
        catch (HttpRequestException ex)
        {
            return ListArchiveFetch.Failed($"NSE could not be reached ({ex.HttpRequestError}).");
        }
    }

    [LoggerMessage(EventId = 3142, Level = LogLevel.Information, Message = "history.lists answered host={Host} path={Path} status={Status}")]
    private static partial void LogAnswered(ILogger logger, string host, string path, int status);
}
