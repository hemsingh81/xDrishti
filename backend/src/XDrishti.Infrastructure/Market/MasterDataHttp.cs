using System.Net;
using System.Net.Http.Headers;

namespace XDrishti.Infrastructure.Market;

/// <summary>Settings of the master-data HTTP clients: public files only, no credentials, no cookies, no redirects, size and time limits.</summary>
internal static class MasterDataHttp
{
    /// <summary>Identifies xDrishti to the file servers (NSE's archive refuses clients without a User-Agent).</summary>
    public const string UserAgent = "xDrishti/0.1 (personal use)";

    public const long DhanMaxBytes = 100L * 1024 * 1024;

    public const long NseMaxBytes = 5L * 1024 * 1024;

    public static readonly TimeSpan DhanTimeout = TimeSpan.FromSeconds(120);

    public static readonly TimeSpan NseTimeout = TimeSpan.FromSeconds(30);

    public static void ConfigureDhan(HttpClient client) => Configure(client);

    public static void ConfigureNse(HttpClient client) => Configure(client);

    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        // A redirect would leave the allow-list unchecked (it is followed inside this handler): never follow one.
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    // HttpClient.Timeout stays infinite: the deadline of each download (above) covers connecting, the headers and the whole body, on the app's clock.
    private static void Configure(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
    }
}
