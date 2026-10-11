using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Tests.Market;

/// <summary>The two master-data hosts behind the real allow-list guard and client settings: records every request that got through and answers as configured.</summary>
internal sealed class StubMasterData : HttpMessageHandler, IHttpClientFactory
{
    public List<(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers)> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") };

    /// <summary>When set, answers instead of <see cref="Respond"/> (can wait, honouring the cancellation token).</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? RespondAsync { get; set; }

    public FakeTimeProvider Clock { get; } = new();

    public static HttpResponseMessage Csv(string text, string? etag = null, string? lastModified = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/csv") };
        if (etag is not null)
        {
            response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
        }

        if (lastModified is not null)
        {
            response.Content.Headers.LastModified = DateTimeOffset.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture);
        }

        return response;
    }

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(new MasterDataEndpointGuard { InnerHandler = this }, disposeHandler: false);
        if (name == MasterDataHttpClientNames.Dhan)
        {
            MasterDataHttp.ConfigureDhan(client);
        }
        else
        {
            MasterDataHttp.ConfigureNse(client);
        }

        return client;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        headers["User-Agent"] = request.Headers.UserAgent.ToString();
        Requests.Add((request.Method, request.RequestUri!, headers));
        return RespondAsync is { } later ? await later(request, cancellationToken) : Respond(request);
    }
}
