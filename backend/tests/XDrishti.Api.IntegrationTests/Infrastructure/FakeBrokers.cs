using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A fake Dhan server behind the API's real broker HTTP client (only its primary handler is replaced, so the allow-list guard still runs).
/// Records every request that reached it and answers like Dhan's documentation, with made-up values; every token and code carries <see cref="AccountsApi.Marker"/>. Tests may override the answer per endpoint.
/// </summary>
public sealed class FakeBrokers
{
    public const string DhanAccessToken = "dhan-access-" + AccountsApi.Marker;

    public const string ConsentAppId = "consent-app-0001";

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    private readonly ConcurrentDictionary<string, Func<RecordedRequest, HttpResponseMessage>> _overrides = new();

    public IReadOnlyCollection<RecordedRequest> Requests => _requests;

    /// <summary>What the fake Dhan reports at <c>GET /v2/holdings</c> (for a request that carries the fake access token).</summary>
    public List<FakeHolding> Holdings { get; } = [];

    /// <summary>Runs before the fake Dhan answers a holdings request (a test holds the answer back to make a sync slow).</summary>
    public Func<Task>? BeforeHoldingsAnswer { get; set; }

    /// <summary>The client id Dhan reports for every token.</summary>
    public string DhanClientId { get; set; } = AccountsApi.ClientId;

    /// <summary>The API's clock (Dhan states expiry times relative to it).</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Reset()
    {
        _requests.Clear();
        _overrides.Clear();
        Holdings.Clear();
        BeforeHoldingsAnswer = null;
        DhanClientId = AccountsApi.ClientId;
    }

    /// <summary>Answers requests to <paramref name="path"/> (for example <c>/app/consumeApp-consent</c>) with <paramref name="respond"/> from now on.</summary>
    public void Override(string path, Func<RecordedRequest, HttpResponseMessage> respond) => _overrides[path] = respond;

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private HttpResponseMessage Answer(RecordedRequest request)
    {
        if (_overrides.TryGetValue(request.Uri.AbsolutePath, out var respond))
        {
            return respond(request);
        }

        var dhanExpiry = Clock.GetUtcNow().ToOffset(Ist).AddHours(20);
        return request.Uri.AbsolutePath switch
        {
            "/app/generate-consent" => Json(HttpStatusCode.OK, $$"""{"consentAppId":"{{ConsentAppId}}","consentAppStatus":"GENERATED","status":"success"}"""),
            "/app/consumeApp-consent" => Json(HttpStatusCode.OK, $$"""
                {"dhanClientId":"{{DhanClientId}}","dhanClientName":"Example Holder","dhanClientUcc":"ABCD1234","givenPowerOfAttorney":false,
                 "accessToken":"{{DhanAccessToken}}","expiryTime":"{{dhanExpiry.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}}"}
                """),
            "/v2/holdings" => request.Header("access-token") == DhanAccessToken
                ? Json(HttpStatusCode.OK, HoldingsJson())
                : Json(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}"""),
            _ => Json(HttpStatusCode.NotFound, """{"errorCode":"NOT_FAKED"}"""),
        };
    }

    private string HoldingsJson() => "[" + string.Join(",", Holdings.ToArray().Select(h => h.ToJson())) + "]";

    /// <summary>One holding as Dhan reports it (made-up values; Exchange "ALL" like the documentation's example).</summary>
    public sealed record FakeHolding(string SecurityId, string Symbol, string? Isin, long Qty, decimal AvgCost, decimal Ltp, string Exchange = "ALL")
    {
        public string ToJson() => string.Create(CultureInfo.InvariantCulture, $$"""
            {"exchange":"{{Exchange}}","tradingSymbol":"{{Symbol}}","securityId":"{{SecurityId}}","isin":{{(Isin is null ? "null" : $"\"{Isin}\"")}},"totalQty":{{Qty}},"dpQty":{{Qty}},"t1Qty":0,"mtf_t1_qty":0,"mtf_qty":0,"availableQty":{{Qty}},"collateralQty":0,"avgCostPrice":{{AvgCost}},"lastTradedPrice":{{Ltp}}}
            """);
    }

    /// <summary>One request as Dhan saw it.</summary>
    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body)
    {
        public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : string.Empty;
    }

    private sealed class Handler(FakeBrokers brokers) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            string? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                headers["Content-Type"] = request.Content.Headers.ContentType?.MediaType ?? string.Empty;
            }

            var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, body);
            brokers._requests.Enqueue(recorded);
            if (recorded.Uri.AbsolutePath == "/v2/holdings" && brokers.BeforeHoldingsAnswer is { } wait)
            {
                await wait();
            }

            return brokers.Answer(recorded);
        }
    }
}
