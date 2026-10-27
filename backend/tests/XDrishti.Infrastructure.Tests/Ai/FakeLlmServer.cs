using System.Net;
using System.Text;

namespace XDrishti.Infrastructure.Tests.Ai;

/// <summary>A stand-in for the llama.cpp server: answers by path, counts requests, can refuse, stall or redirect. Used as the primary handler of an <see cref="IHttpClientFactory"/> stub.</summary>
internal sealed class FakeLlmServer : HttpMessageHandler, IHttpClientFactory
{
    public const string Key = "k-test-0123456789abcdef0123456789ab";

    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    public List<(string Path, string? Authorization)> Requests { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The healthy server: /health 200, /v1/models lists the alias (key required), /props names the file and build.</summary>
    public static FakeLlmServer Healthy(string alias = "xd-main", string modelPath = "/mnt/models/Qwen3-30B-A3B-Q4_K_M.gguf", string build = "b11530-abc") => new()
    {
        Handler = request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/health" => Json(HttpStatusCode.OK, """{"status":"ok"}"""),
            "/v1/models" when request.Headers.Authorization?.Parameter == Key => Json(HttpStatusCode.OK, $$"""{"object":"list","data":[{"id":"{{alias}}"}]}"""),
            "/props" when request.Headers.Authorization?.Parameter == Key => Json(HttpStatusCode.OK, $$"""{"model_path":"{{modelPath}}","build_info":"{{build}}"}"""),
            _ => Json(HttpStatusCode.Unauthorized, """{"error":"no"}"""),
        }),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));
        }

        return await Handler(request).WaitAsync(cancellationToken);
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
}
