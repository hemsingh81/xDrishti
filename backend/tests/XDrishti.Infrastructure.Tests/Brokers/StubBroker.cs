using System.Net;
using System.Text;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>A fake broker behind the real allow-list guard: records every request that got through and answers with the configured response.</summary>
internal sealed class StubBroker : HttpMessageHandler, IHttpClientFactory
{
    public List<(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body)> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, "{}");

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(new BrokerEndpointGuard { InnerHandler = this }, disposeHandler: false);
        if (name == BrokerHttpClientNames.DhanHoldings)
        {
            BrokerHttp.ConfigureHoldings(client);
        }
        else if (name == BrokerHttpClientNames.DhanChart)
        {
            BrokerHttp.ConfigureChart(client);
        }
        else if (name == BrokerHttpClientNames.DhanQuote)
        {
            BrokerHttp.ConfigureQuote(client);
        }
        else
        {
            BrokerHttp.Configure(client);
        }

        return client;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.Content?.Headers.ContentType is { } type)
        {
            headers["Content-Type"] = type.MediaType ?? string.Empty;
        }

        Requests.Add((request.Method, request.RequestUri!, headers, body));
        return Respond(request);
    }
}
