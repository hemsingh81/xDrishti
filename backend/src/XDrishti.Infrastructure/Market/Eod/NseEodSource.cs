using System.IO.Compression;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Market.Eod;

namespace XDrishti.Infrastructure.Market.Eod;

/// <summary>
/// GET of one NSE end-of-day file through the guarded history client (the allow-list is read here, never written: a path that is not on it is refused before sending, <see cref="EodFetchStatus.NotAllowed"/>). No cookies, no redirects, size and time
/// caps; a zip is unpacked in memory with a cap on the unpacked size. Only the host, path and HTTP status are logged, never content. A 404 is "no such file" (a holiday), any other non-200 is a failure; nothing is worked around.
/// </summary>
internal sealed partial class NseEodSource(IHttpClientFactory clients, TimeProvider clock, ILogger<NseEodSource> logger) : IEodSource
{
    public const long MaxDownloadBytes = 50L * 1024 * 1024;

    public const long MaxUnpackedBytes = 200L * 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async ValueTask<EodFetch> FetchAsync(EodFileKind kind, DateOnly date, CancellationToken cancellationToken)
    {
        var path = kind == EodFileKind.Bhavcopy ? EodArchive.BhavcopyPath(date) : EodArchive.DeliveryPath(date);
        using var deadline = new CancellationTokenSource(Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{EodArchive.Host}{path}"));
            using var response = await clients.CreateClient(MasterDataHttpClientNames.NseHistory).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            LogAnswered(logger, EodArchive.Host, path, (int)response.StatusCode);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return EodFetch.Absent();
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return EodFetch.Failed($"NSE answered HTTP {(int)response.StatusCode} for {path}.");
            }

            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            {
                return EodFetch.Failed($"The file {path} is larger than the allowed {MaxDownloadBytes / (1024 * 1024)} MB.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
            var bytes = await ReadCappedAsync(body, MaxDownloadBytes, linked.Token);
            if (bytes is null)
            {
                return EodFetch.Failed($"The file {path} is larger than the allowed {MaxDownloadBytes / (1024 * 1024)} MB.");
            }

            return await UnpackAsync(bytes, path, linked.Token);
        }
        catch (MasterDataEndpointNotAllowedException ex)
        {
            return EodFetch.NotAllowed($"{ex.Message} (host {EodArchive.Host}, path {path})");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return EodFetch.Failed($"NSE did not answer in time for {path}.");
        }
        catch (HttpRequestException ex)
        {
            // The exception text is not shown: it can name the request URL.
            return EodFetch.Failed($"NSE could not be reached ({ex.HttpRequestError}).");
        }
    }

    // A zip (magic PK) is unpacked: its first entry, capped. Anything else is the file itself.
    internal static async ValueTask<EodFetch> UnpackAsync(byte[] bytes, string path, CancellationToken cancellationToken)
    {
        if (bytes.Length < 4 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
        {
            return EodFetch.Ok(bytes);
        }

        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Length > 0);
            if (entry is null || entry.Length > MaxUnpackedBytes)
            {
                return EodFetch.Failed($"The zip {path} is empty or too large.");
            }

            await using var stream = await entry.OpenAsync(cancellationToken);
            var unpacked = await ReadCappedAsync(stream, MaxUnpackedBytes, cancellationToken);
            return unpacked is null ? EodFetch.Failed($"The zip {path} is too large.") : EodFetch.Ok(unpacked);
        }
        catch (InvalidDataException)
        {
            return EodFetch.Failed($"The file {path} is not a readable zip.");
        }
    }

    private static async ValueTask<byte[]?> ReadCappedAsync(Stream stream, long cap, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > cap)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    [LoggerMessage(EventId = 3111, Level = LogLevel.Information, Message = "history.nse answered host={Host} path={Path} status={Status}")]
    private static partial void LogAnswered(ILogger logger, string host, string path, int status);
}
