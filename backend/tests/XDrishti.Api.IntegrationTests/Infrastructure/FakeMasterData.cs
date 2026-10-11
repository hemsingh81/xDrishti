using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Fake Dhan and NSE file servers behind the API's real master-data HTTP clients (only the primary handler is replaced, so the allow-list guard and
/// the client settings stay as in production). Serves the committed samples, honours <c>If-None-Match</c> like Dhan's CDN does, records every request.
/// </summary>
public sealed class FakeMasterData
{
    public const string DhanPath = "/api-data/api-scrip-master-detailed.csv";

    public const string NsePath = "/content/indices/ind_niftytotalmarket_list.csv";

    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public FakeMasterData() => Reset();

    public IReadOnlyCollection<RecordedRequest> Requests => _requests;

    public string MasterCsv { get; set; } = string.Empty;

    public string MasterETag { get; set; } = string.Empty;

    public string SectorCsv { get; set; } = string.Empty;

    /// <summary>Replaces the answer for the master file (return null to answer normally).</summary>
    public Func<RecordedRequest, HttpResponseMessage?>? MasterOverride { get; set; }

    public Func<RecordedRequest, HttpResponseMessage?>? SectorOverride { get; set; }

    public IEnumerable<RecordedRequest> MasterRequests => _requests.Where(r => r.Uri.AbsolutePath == DhanPath);

    public IEnumerable<RecordedRequest> SectorRequests => _requests.Where(r => r.Uri.AbsolutePath == NsePath);

    public static string Fixture(string name)
    {
        using var stream = typeof(FakeMasterData).Assembly.GetManifestResourceStream("Fixtures/" + name)
            ?? throw new InvalidOperationException($"Fixture {name} is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Reset()
    {
        _requests.Clear();
        MasterCsv = Fixture("dhan-master-sample.csv");
        SectorCsv = Fixture("nse-sector-sample.csv");
        MasterETag = "\"dhan-v1\"";
        MasterOverride = null;
        SectorOverride = null;
    }

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private HttpResponseMessage Answer(RecordedRequest request)
    {
        if (request.Uri.AbsolutePath == DhanPath)
        {
            if (MasterOverride?.Invoke(request) is { } custom)
            {
                return custom;
            }

            if (request.Header("If-None-Match") == MasterETag)
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MasterCsv, Encoding.UTF8, "application/octet-stream") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(MasterETag);
            return response;
        }

        if (request.Uri.AbsolutePath == NsePath)
        {
            if (SectorOverride?.Invoke(request) is { } custom)
            {
                return custom;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SectorCsv, Encoding.UTF8, "text/csv") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"49439-1\"", isWeak: true);
            return response;
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>One request as the file server saw it.</summary>
    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers)
    {
        public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : string.Empty;
    }

    private sealed class Handler(FakeMasterData data) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            headers["User-Agent"] = request.Headers.UserAgent.ToString();
            var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers);
            data._requests.Enqueue(recorded);
            return Task.FromResult(data.Answer(recorded));
        }
    }
}
