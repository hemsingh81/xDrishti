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

    /// <summary>Forgets the requests seen so far (a test looks only at what follows).</summary>
    public void ClearRequests() => _requests.Clear();

    /// <summary>What the fake Dhan reports at <c>GET /v2/holdings</c> (for a request that carries the fake access token).</summary>
    public List<FakeHolding> Holdings { get; } = [];

    /// <summary>What the fake Dhan quotes at <c>POST /v2/marketfeed/quote</c> by Dhan security id (last price, previous close; a null close is left out of the answer). Needs the fake access token and a client id.</summary>
    public Dictionary<string, (decimal Last, decimal? Close)> Quotes { get; } = [];

    /// <summary>What the fake Dhan reports at <c>GET /v2/trades/{from}/{to}/{page}</c> (those whose date is in the asked dates, a page of <see cref="TradePageSize"/> at a time; for a request that carries the fake access token).</summary>
    public List<FakeTrade> Trades { get; } = [];

    public int TradePageSize { get; set; } = 100;

    /// <summary>Trade rows sent exactly as written (a row that does not read, to see how it is dropped), listed with the others by their date and the <c>exchangeTradeId</c> text in the row's key.</summary>
    public List<(DateOnly Date, string Key, string Json)> RawTrades { get; } = [];

    /// <summary>Ledger rows sent exactly as written, for the dates asked.</summary>
    public List<(DateOnly Date, string Json)> RawLedger { get; } = [];

    /// <summary>What the fake Dhan reports at <c>GET /v2/ledger</c> (those whose date is in the asked dates).</summary>
    public List<FakeLedgerLine> Ledger { get; } = [];

    /// <summary>When true an empty page of trades is Dhan's documented data error DH-907 (a 400) instead of an empty list.</summary>
    public bool EmptyPageIsNoDataError { get; set; }

    /// <summary>Answers a request of the trade history or the ledger instead of the fake data (return null to let the normal answer go out).</summary>
    public Func<RecordedRequest, HttpResponseMessage?>? HistoryOverride { get; set; }

    /// <summary>Runs before the fake Dhan answers a trade history request (a test holds the answer back to make a run slow).</summary>
    public Func<Task>? BeforeTradesAnswer { get; set; }

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
        Quotes.Clear();
        Trades.Clear();
        Ledger.Clear();
        RawTrades.Clear();
        RawLedger.Clear();
        TradePageSize = 100;
        EmptyPageIsNoDataError = false;
        HistoryOverride = null;
        BeforeTradesAnswer = null;
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

        if (request.Uri.AbsolutePath.StartsWith("/v2/trades/", StringComparison.Ordinal) || request.Uri.AbsolutePath == "/v2/ledger")
        {
            return HistoryOverride?.Invoke(request) ?? HistoryAnswer(request);
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
            "/v2/marketfeed/quote" => request.Header("access-token") == DhanAccessToken && request.Header("client-id").Length > 0
                ? Json(HttpStatusCode.OK, QuoteJson(request.Body))
                : Json(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}"""),
            _ => Json(HttpStatusCode.NotFound, """{"errorCode":"NOT_FAKED"}"""),
        };
    }

    private static HttpResponseMessage Refused() =>
        Json(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}""");

    /// <summary>The trade history (<c>/v2/trades/{from}/{to}/{page}</c>) and the ledger (<c>/v2/ledger?from-date=…&amp;to-date=…</c>) as Dhan's documentation shows them, with made-up values.</summary>
    private HttpResponseMessage HistoryAnswer(RecordedRequest request)
    {
        if (request.Header("access-token") != DhanAccessToken)
        {
            return Refused();
        }

        var invariant = CultureInfo.InvariantCulture;
        if (request.Uri.AbsolutePath == "/v2/ledger")
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.Uri.Query);
            var from = DateOnly.ParseExact(query["from-date"]!, "yyyy-MM-dd", invariant);
            var to = DateOnly.ParseExact(query["to-date"]!, "yyyy-MM-dd", invariant);
            return Json(HttpStatusCode.OK, "[" + string.Join(",", Ledger.ToArray().Where(l => l.Date >= from && l.Date <= to).Select(l => l.ToJson()).Concat(RawLedger.ToArray().Where(l => l.Date >= from && l.Date <= to).Select(l => l.Json))) + "]");
        }

        var parts = request.Uri.AbsolutePath["/v2/trades/".Length..].Split('/');
        var fromDate = DateOnly.ParseExact(parts[0], "yyyy-MM-dd", invariant);
        var toDate = DateOnly.ParseExact(parts[1], "yyyy-MM-dd", invariant);
        var page = int.Parse(parts[2], invariant);
        var rows = Trades.ToArray().Select(t => (t.Date, Key: t.TradeId, Json: t.ToJson())).Concat(RawTrades.ToArray()).Where(t => t.Date >= fromDate && t.Date <= toDate).OrderBy(t => t.Date).ThenBy(t => t.Key, StringComparer.Ordinal).Skip(page * TradePageSize).Take(TradePageSize).ToList();
        return rows.Count == 0 && EmptyPageIsNoDataError
            ? Json(HttpStatusCode.BadRequest, """{"errorType":"Data_API_Error","errorCode":"DH-907","errorMessage":"Incorrect parameters or no data present"}""")
            : Json(HttpStatusCode.OK, "[" + string.Join(",", rows.Select(t => t.Json)) + "]");
    }

    /// <summary>Dhan's documented market-quote answer for the asked NSE_EQ ids that <see cref="Quotes"/> knows (made-up values).</summary>
    private string QuoteJson(string? body)
    {
        var asked = System.Text.Json.JsonDocument.Parse(body ?? "{}").RootElement.GetProperty("NSE_EQ").EnumerateArray().Select(e => e.GetInt64().ToString(CultureInfo.InvariantCulture));
        var entries = asked.Where(Quotes.ContainsKey).Select(id =>
        {
            var (last, close) = Quotes[id];
            var ohlc = close is { } c ? string.Create(CultureInfo.InvariantCulture, $"\"ohlc\":{{\"open\":0,\"high\":0,\"low\":0,\"close\":{c}}},") : string.Empty;
            return string.Create(CultureInfo.InvariantCulture, $"\"{id}\":{{{ohlc}\"last_price\":{last},\"volume\":0}}");
        });
        return "{\"data\":{\"NSE_EQ\":{" + string.Join(",", entries) + "}},\"status\":\"success\"}";
    }

    private string HoldingsJson() => "[" + string.Join(",", Holdings.ToArray().Select(h => h.ToJson())) + "]";

    /// <summary>One holding as Dhan reports it (made-up values; Exchange "ALL" like the documentation's example).</summary>
    public sealed record FakeHolding(string SecurityId, string Symbol, string? Isin, long Qty, decimal AvgCost, decimal Ltp, string Exchange = "ALL")
    {
        public string ToJson() => string.Create(CultureInfo.InvariantCulture, $$"""
            {"exchange":"{{Exchange}}","tradingSymbol":"{{Symbol}}","securityId":"{{SecurityId}}","isin":{{(Isin is null ? "null" : $"\"{Isin}\"")}},"totalQty":{{Qty}},"dpQty":{{Qty}},"t1Qty":0,"mtf_t1_qty":0,"mtf_qty":0,"availableQty":{{Qty}},"collateralQty":0,"avgCostPrice":{{AvgCost}},"lastTradedPrice":{{Ltp}}}
            """);
    }

    /// <summary>One trade as the trade history reports it (made-up values; segment and product as Dhan names them).</summary>
    public sealed record FakeTrade(
        string TradeId, string? Symbol, string Side, long Qty, decimal Price, DateOnly Date, TimeOnly Time, string Segment = "NSE_EQ", string Product = "CNC", string SecurityId = "1001", string? Isin = null, decimal Brokerage = 0m)
    {
        /// <summary>A null <see cref="Symbol"/> is sent as <c>"tradingSymbol": null</c> with a company name as <c>customSymbol</c>, like Dhan's published sample.</summary>
        public string ToJson() => string.Create(CultureInfo.InvariantCulture, $$"""
            {"dhanClientId":"1000000001","orderId":"O-{{TradeId}}","exchangeOrderId":"X-{{TradeId}}","exchangeTradeId":"{{TradeId}}","transactionType":"{{Side}}","exchangeSegment":"{{Segment}}","productType":"{{Product}}","orderType":"LIMIT","tradingSymbol":{{(Symbol is null ? "null" : "\"" + Symbol + "\"")}},"customSymbol":"{{Symbol ?? "Example Industries"}}","securityId":"{{SecurityId}}",{{(Isin is null ? string.Empty : "\"isin\":\"" + Isin + "\",")}}{{(Brokerage == 0m ? string.Empty : "\"brokerageCharges\":" + Brokerage.ToString(CultureInfo.InvariantCulture) + ",\"stt\":\"0.5\",")}}"tradedQuantity":{{Qty}},"tradedPrice":{{Price}},"createTime":"{{Date:yyyy-MM-dd}} {{Time:HH:mm:ss}}","updateTime":"{{Date:yyyy-MM-dd}} {{Time:HH:mm:ss}}","exchangeTime":"{{Date:yyyy-MM-dd}} {{Time:HH:mm:ss}}","drvExpiryDate":null,"drvOptionType":null,"drvStrikePrice":0}
            """);
    }

    /// <summary>One ledger line as the ledger reports it (made-up values; amounts as text, the date as <c>Sep 2, 2026</c>).</summary>
    public sealed record FakeLedgerLine(DateOnly Date, string VoucherNo, string Description, string Narration, decimal Debit, decimal Credit, decimal Balance)
    {
        public string ToJson() => string.Create(CultureInfo.InvariantCulture, $$"""
            {"dhanClientId":"1000000001","narration":"{{Narration}}","voucherdate":"{{Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}}","exchange":"NSE-CAPITAL","voucherdesc":"{{Description}}","vouchernumber":"{{VoucherNo}}","debit":"{{Debit:0.00}}","credit":"{{Credit:0.00}}","runbal":"{{Balance:0.00}}"}
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

            if (recorded.Uri.AbsolutePath.StartsWith("/v2/trades/", StringComparison.Ordinal) && brokers.BeforeTradesAnswer is { } waitForTrades)
            {
                await waitForTrades();
            }

            return brokers.Answer(recorded);
        }
    }
}
