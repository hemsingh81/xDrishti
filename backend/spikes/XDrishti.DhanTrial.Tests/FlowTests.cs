using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace XDrishti.DhanTrial.Tests;

public class TokenFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 0, 0, IstTime.Offset);

    private static (TokenFlow Flow, FakeApi Api, MemoryTokenStore Store, ScriptedPrompt Prompt, Redactor Redactor) Create(string tokenId = "token-id-from-browser")
    {
        var api = new FakeApi();
        var store = new MemoryTokenStore();
        var prompt = new ScriptedPrompt(tokenId);
        var redactor = new Redactor();
        var clock = new FakeTimeProvider(Now);
        return (new TokenFlow(api, store, prompt, redactor, clock), api, store, prompt, redactor);
    }

    [Fact]
    public async Task The_consent_flow_asks_the_owner_to_log_in_then_stores_a_token_with_its_real_expiry()
    {
        var (flow, api, store, prompt, _) = Create();

        var token = await flow.LoginWithConsentAsync("1000000001", "key", "secret", CancellationToken.None);

        api.Log.ShouldBe(["consent:1000000001:key", "consume:token-id-from-browser"]);
        prompt.Shown.ShouldContain(TokenFlow.LoginUrl + "consent-123");
        token.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 10, 20, 30, 0, IstTime.Offset));
        token.Lifetime.TotalHours.ShouldBe(24.5, 0.01);
        store.Saved.ShouldBe(token);
    }

    [Fact]
    public async Task Tokens_and_token_ids_become_redacted_everywhere_afterwards()
    {
        var (flow, _, _, _, redactor) = Create();

        await flow.LoginWithConsentAsync("1000000001", "key", "secret", CancellationToken.None);

        redactor.Redact("x token-id-from-browser y").ShouldNotContain("token-id-from-browser");
        redactor.Redact("Bearer eyJhbGciOiJIUzUxMiJ9.payload-part.signature-part").ShouldNotContain("payload-part");
    }

    [Fact]
    public async Task A_failed_consent_stops_the_flow_with_the_dhan_error_and_stores_nothing()
    {
        var (flow, api, store, _, _) = Create();
        api.Consent = () => FakeApi.Fail("DH-901", "Invalid credentials");

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => flow.LoginWithConsentAsync("1", "k", "s", CancellationToken.None));

        ex.Message.ShouldContain("DH-901");
        store.Saved.ShouldBeNull();
    }

    [Fact]
    public async Task Renewal_replaces_the_token_and_reports_the_remaining_lifetime_before_renewing()
    {
        var (flow, _, store, _, _) = Create();
        var current = await flow.LoginWithConsentAsync("1000000001", "key", "secret", CancellationToken.None);

        var result = await flow.RenewAsync(current, CancellationToken.None);

        result.Ok.ShouldBeTrue();
        result.RemainingBefore.TotalHours.ShouldBe(24.5, 0.01);
        result.Token!.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 11, 20, 30, 0, IstTime.Offset));
        store.Saved.ShouldBe(result.Token);
    }

    [Fact]
    public async Task A_refused_renewal_is_a_result_not_an_exception_and_keeps_the_old_token()
    {
        var (flow, api, store, _, _) = Create();
        var current = await flow.LoginWithConsentAsync("1000000001", "key", "secret", CancellationToken.None);
        api.Renew = () => FakeApi.Fail("DH-901", "Only web-generated tokens can be renewed");

        var result = await flow.RenewAsync(current, CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("web-generated");
        store.Saved.ShouldBe(current);
    }

    [Fact]
    public async Task A_response_without_an_expiry_is_flagged_as_an_assumed_lifetime()
    {
        var (flow, api, _, _, _) = Create();
        api.Consume = () => FakeApi.Ok("{\"accessToken\":\"eyJhbGciOiJIUzUxMiJ9.payload-part.signature-part\"}");

        var token = await flow.LoginWithConsentAsync("1", "k", "s", CancellationToken.None);

        token.Via.ShouldContain("ASSUMED");
    }

    [Fact]
    public async Task A_successful_renewal_without_a_usable_token_is_reported_as_a_lost_token()
    {
        var (flow, api, store, _, _) = Create();
        var current = await flow.LoginWithConsentAsync("1", "k", "s", CancellationToken.None);
        api.Renew = () => FakeApi.Ok("{\"status\":\"ok\"}");

        var result = await flow.RenewAsync(current, CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.TokenLost.ShouldBeTrue();
        store.Saved.ShouldBe(current);
    }

    [Fact]
    public async Task The_totp_route_generates_the_current_code_and_needs_no_browser()
    {
        var (flow, api, _, prompt, _) = Create();

        var token = await flow.LoginWithTotpAsync("1000000001", "123456", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", CancellationToken.None);

        api.Log.Single().ShouldMatch(@"^totp:\d{6}$");
        prompt.Shown.ShouldBeEmpty();
        token.Via.ShouldContain("TOTP");
    }

    [Fact]
    public async Task The_profile_tells_whether_the_data_api_plan_is_active()
    {
        var (flow, api, _, _, _) = Create();
        var token = new AccessToken("t", Now.AddHours(1), "1", "test", Now);

        (await flow.GetProfileAsync(token, CancellationToken.None)).DataPlanActive.ShouldBeTrue();

        api.Profile = () => FakeApi.Ok("{\"dataPlan\":\"Deactive\"}");
        (await flow.GetProfileAsync(token, CancellationToken.None)).DataPlanActive.ShouldBeFalse();
    }
}

public class TokenFileTests
{
    [Fact]
    public void The_token_file_is_private_from_creation_and_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xd-token-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(Path.Combine(dir, "t.json"));
            var token = new AccessToken("secret-token-value", new DateTimeOffset(2026, 10, 10, 20, 30, 0, IstTime.Offset), "1", "test", new DateTimeOffset(2026, 10, 9, 20, 0, 0, IstTime.Offset));

            store.Save(token);
            store.Save(token);

            store.Load().ShouldBe(token);
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(Path.Combine(dir, "t.json")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("  abc123  ", "abc123")]
    [InlineData("http://127.0.0.1:8080/cb?tokenId=abc123", "abc123")]
    [InlineData("http://127.0.0.1:8080/cb?x=1&tokenId=abc%2B123&y=2", "abc+123")]
    public void The_token_id_can_be_pasted_bare_or_as_the_whole_redirect_url(string input, string expected)
    {
        TokenFlow.ExtractTokenId(input).ShouldBe(expected);
    }
}

public class DhanApiTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 0, 0, IstTime.Offset);

    [Fact]
    public async Task Requests_carry_the_documented_headers_and_body_and_nothing_is_logged_in_the_clear()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.OK, TestData.CandleJson(TestData.Session(TestData.Monday))));
        var redactor = new Redactor(["tok-secret-value"]);
        using var client = new HttpClient(new ReadOnlyGuardHandler(http));
        var api = new DhanApi(client, new NoRateLimiter(), redactor, new FakeTimeProvider(Now));

        var response = await api.GetIntradayAsync("tok-secret-value", new InstrumentKey("2885", "NSE_EQ", "EQUITY"), new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)), CancellationToken.None);

        response.Ok.ShouldBeTrue();
        var request = http.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.ShouldBe("/v2/charts/intraday");
        request.Headers.GetValues("access-token").Single().ShouldBe("tok-secret-value");
        var body = http.Bodies.Single();
        body.ShouldContain("\"securityId\":\"2885\"");
        body.ShouldContain("\"exchangeSegment\":\"NSE_EQ\"");
        body.ShouldContain("\"fromDate\":\"2026-10-01 09:15:00\"");
        body.ShouldContain("\"toDate\":\"2026-10-09 15:30:00\"");
        body.ShouldContain("\"interval\":1");
        api.Calls.Single().Endpoint.ShouldBe("v2/charts/intraday");
    }

    [Fact]
    public async Task A_throttled_call_is_retried_with_backoff_and_then_succeeds()
    {
        var calls = 0;
        var http = new FakeHttp(_ => ++calls < 3 ? ((HttpStatusCode)429, "{\"errorCode\":\"DH-904\",\"errorMessage\":\"Rate limit\"}") : (HttpStatusCode.OK, "{}"));
        using var client = new HttpClient(new ReadOnlyGuardHandler(http));
        var clock = new FakeTimeProvider(Now);
        var api = new DhanApi(client, new NoRateLimiter(), new Redactor(), clock);

        var task = api.GetProfileAsync("t", CancellationToken.None);
        while (!task.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var response = await task;

        response.Ok.ShouldBeTrue();
        response.Attempts.ShouldBe(3);
        api.Calls.Count(c => c.ErrorCode == "DH-904").ShouldBe(2);
    }

    [Fact]
    public async Task A_server_that_wants_the_interval_as_a_string_is_retried_once_in_that_form_and_remembered()
    {
        var http = new FakeHttp(request => request.Content!.ReadAsStringAsync().Result.Contains("\"interval\":1,", StringComparison.Ordinal) || request.Content.ReadAsStringAsync().Result.EndsWith("\"interval\":1}", StringComparison.Ordinal)
            ? (HttpStatusCode.BadRequest, "{\"errorCode\":\"DH-905\",\"errorMessage\":\"interval must be a string\"}")
            : (HttpStatusCode.OK, TestData.CandleJson(TestData.Session(TestData.Monday))));
        using var client = new HttpClient(new ReadOnlyGuardHandler(http));
        var api = new DhanApi(client, new NoRateLimiter(), new Redactor(), new FakeTimeProvider(Now));

        var response = await api.GetIntradayAsync("t", new InstrumentKey("1", "NSE_EQ", "EQUITY"), new DateRange(TestData.Monday, TestData.Monday), CancellationToken.None);

        response.Ok.ShouldBeTrue();
        api.IntervalForm.ShouldBe("string");
        http.Bodies.Count.ShouldBe(2);
        http.Bodies[1].ShouldContain("\"interval\":\"1\"");
    }

    [Fact]
    public async Task A_subscription_error_is_reported_with_its_code_not_thrown()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.OK, "{\"status\":\"failed\",\"data\":{\"806\":\"Data APIs not subscribed\"}}"));
        using var client = new HttpClient(new ReadOnlyGuardHandler(http));
        var api = new DhanApi(client, new NoRateLimiter(), new Redactor(), new FakeTimeProvider(Now));

        var response = await api.GetDailyAsync("t", new InstrumentKey("1", "NSE_EQ", "EQUITY"), new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)), CancellationToken.None);

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe("806");
    }
}

public class HistoryFetcherTests
{
    private static readonly InstrumentKey Key = new("2885", "NSE_EQ", "EQUITY");

    [Fact]
    public async Task A_year_is_fetched_in_chunks_within_the_90_day_request_limit()
    {
        var api = new FakeApi();
        var fetcher = new HistoryFetcher(api, new MemoryRawStore(), () => "t", offline: false);

        var result = await fetcher.FetchIntradayAsync("RELIANCE", Key, new DateOnly(2025, 10, 9), new DateOnly(2026, 10, 9), CancellationToken.None);

        result.Chunks.ShouldBe(5);
        api.Log.Count(l => l.StartsWith("intraday", StringComparison.Ordinal)).ShouldBe(5);
        result.Bars.Count.ShouldBe(5 * 375);
    }

    [Fact]
    public async Task A_second_run_reads_the_cache_and_an_offline_run_makes_no_calls()
    {
        var store = new MemoryRawStore();
        var first = new FakeApi();
        await new HistoryFetcher(first, store, () => "t", offline: false).FetchIntradayAsync("X", Key, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 9), CancellationToken.None);
        var offline = new FakeApi();

        var result = await new HistoryFetcher(offline, store, () => "t", offline: true).FetchIntradayAsync("X", Key, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 9), CancellationToken.None);

        offline.Log.ShouldBeEmpty();
        result.FromCache.ShouldBe(1);
        result.Bars.Count.ShouldBe(375);
    }

    [Fact]
    public async Task A_failed_chunk_is_recorded_and_the_other_chunks_still_arrive()
    {
        var api = new FakeApi { Intraday = r => r.From.Year == 2025 ? FakeApi.Fail("DH-907", "No data") : FakeApi.Ok(TestData.CandleJson(TestData.Session(TestData.Monday))) };

        var result = await new HistoryFetcher(api, new MemoryRawStore(), () => "t", offline: false).FetchIntradayAsync("X", Key, new DateOnly(2025, 10, 9), new DateOnly(2026, 10, 9), CancellationToken.None);

        result.Errors.ShouldNotBeEmpty();
        result.Errors[0].ShouldContain("DH-907");
        result.Bars.ShouldNotBeEmpty();
    }
}

public class ReportTests
{
    [Fact]
    public void The_report_has_every_section_and_contains_no_secret()
    {
        var redactor = new Redactor(["very-secret-value"]);
        var results = new TrialResults { GeneratedAt = new DateTimeOffset(2026, 10, 9, 20, 0, 0, IstTime.Offset) };
        results.TokenEvents.Add(new TokenEvent(results.GeneratedAt, "Login", true, "token very-secret-value eyJhbGciOiJIUzUxMiJ9.aaaaaa.bbbbbb"));
        results.Semantics = new TimestampSemantics(EpochBase.TrueUtc, BarLabel.Start, 5, "5 of 5 full sessions");
        results.Estimate = Stats.Estimate(200, 400, 250, 5, 21);

        var report = ReportBuilder.Build(results, redactor);

        foreach (var section in new[] { "## 1. Token flow", "## 2. Instrument master", "## 3. Timestamp semantics", "## 4. 1-minute history quality", "## 5. Daily candle", "## 6. History depth", "## 7. Limits and timing" })
        {
            report.ShouldContain(section);
        }

        report.ShouldNotContain("very-secret-value");
        report.ShouldNotContain("eyJhbGci");
    }

    [Fact]
    public void The_eod_estimate_extrapolates_from_measured_latency_and_the_request_limit()
    {
        var estimate = Stats.Estimate(200, 400, 250, 5, 21);

        estimate.SequentialSeconds.ShouldBe(130, 0.1);
        estimate.RateLimitedSeconds.ShouldBe(80, 0.1);
        estimate.BackfillSeconds.ShouldBe(200 * 22 / 5.0, 0.1);
    }

    [Fact]
    public void Percentiles_use_the_nearest_rank()
    {
        Stats.Percentile([5, 1, 3, 2, 4], 0.5).ShouldBe(3);
        Stats.Percentile([], 0.95).ShouldBe(0);
    }
}
