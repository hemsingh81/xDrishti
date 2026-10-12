using System.Globalization;
using System.Net;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The Dhan phase with the API's <b>real</b> source and HTTP client, over the fake Dhan server (<see cref="FakeBrokers"/>; only its primary handler is replaced, so the
/// allow-list guard, headers, timeouts and JSON are the production ones): what is sent, what is stored, and how a refused token, an offset problem and a leak are handled.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataDhanClientPipelineTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string ChartPath = "/v2/charts/intraday";

    private static readonly string Token = "dhan-chart-" + AccountsApi.Marker;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Brokers.Reset();
        Factory.Dhan.UseConnectedAccount("Primary", Token);
    }

    private static string Arrays(IReadOnlyList<Bar> bars)
    {
        string Join(Func<Bar, double> value) => string.Join(',', bars.Select(b => value(b).ToString("R", CultureInfo.InvariantCulture)));
        return $$"""{"open":[{{Join(b => b.Open)}}],"high":[{{Join(b => b.High)}}],"low":[{{Join(b => b.Low)}}],"close":[{{Join(b => b.Close)}}],"volume":[{{Join(b => b.Volume)}}],"timestamp":[{{string.Join(',', bars.Select(b => b.EpochSeconds))}}],"open_interest":[{{string.Join(',', bars.Select(_ => "0"))}}]}""";
    }

    private async Task<Guid> TrackedWithBarsAsync(string ticker, List<Bar> bars)
    {
        var id = await SeedAsync(ticker);
        WriteCsv(ticker, bars);
        (await RunAsync(["csv"])).Csv.Status.ShouldBe("succeeded");
        return id;
    }

    [Fact]
    public async Task The_real_client_sends_the_documented_request_for_each_instrument_and_the_bars_are_stored_as_dhan()
    {
        var csvBars = BarFixtures.Session(LastSession, 1, count: 60);
        var abb = await TrackedWithBarsAsync("ABB", csvBars);
        var securityId = await ScalarAsync<string>("SELECT broker_id FROM market.instrument_broker_ids WHERE instrument_id = @id", ("id", abb));
        var overlap = csvBars.Skip(40).ToList();
        var newBars = BarFixtures.Session(new DateOnly(2026, 10, 12), 7, count: 20); // 09:15-09:34 on the pinned Monday, before 12:00
        Factory.Brokers.Override(ChartPath, _ => FakeBrokers.Json(HttpStatusCode.OK, Arrays([.. overlap, .. newBars])));

        var job = await RunAsync(["dhan", "aggregate"]);

        job.Dhan.Status.ShouldBe("succeeded", job.Dhan.Message);
        (job.Dhan.InstrumentsFetched, job.Dhan.Requests, job.Dhan.Bars, job.Dhan.Inserted, job.Dhan.Unchanged).ShouldBe((1, 1, 40, 20, 20));
        (job.Dhan.OverlapCompared, job.Dhan.OverlapMismatched).ShouldBe((20, 0)); // the alignment of the timestamps is proven by the overlap
        var request = Factory.Brokers.Requests.Where(r => r.Uri.AbsolutePath == ChartPath).ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/charts/intraday");
        request.Header("access-token").ShouldBe(Token);
        request.Header("Content-Type").ShouldBe("application/json");
        request.Headers.Keys.ShouldNotContain("client-id");
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        (root.GetProperty("securityId").GetString(), root.GetProperty("exchangeSegment").GetString(), root.GetProperty("instrument").GetString())
            .ShouldBe((securityId, "NSE_EQ", "EQUITY"));
        (root.GetProperty("interval").GetInt32(), root.GetProperty("oi").GetBoolean()).ShouldBe((1, false));
        var lastBar = csvBars[^1].EpochSeconds;
        var from = DateTimeOffset.FromUnixTimeSeconds(lastBar - (2 * 86400)).ToOffset(TimeSpan.FromMinutes(330));
        root.GetProperty("fromDate").GetString().ShouldBe(from.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        root.GetProperty("toDate").GetString().ShouldBe("2026-10-12 12:00:00");
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m WHERE source = 'dhan'")).ShouldBe(40);
        (await StoredBarsAsync(abb)).Count.ShouldBe(80);
    }

    [Fact]
    public async Task A_first_fetch_without_bars_goes_back_the_initial_days_in_windows_of_at_most_90_days_one_request_each()
    {
        var abb = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { abb } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        Factory.Brokers.Override(ChartPath, _ => FakeBrokers.Json(HttpStatusCode.OK, Arrays([])));

        var job = await RunAsync(["dhan"]);

        job.Dhan.Status.ShouldBe("succeeded", job.Dhan.Message);
        var requests = Factory.Brokers.Requests.Where(r => r.Uri.AbsolutePath == ChartPath).ToList();
        requests.Count.ShouldBe(5); // 365 days in windows of 90
        (job.Dhan.Requests, job.Dhan.Bars).ShouldBe((5, 0));
        foreach (var request in requests)
        {
            using var body = JsonDocument.Parse(request.Body!);
            var from = DateTime.ParseExact(body.RootElement.GetProperty("fromDate").GetString()!, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var to = DateTime.ParseExact(body.RootElement.GetProperty("toDate").GetString()!, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            (to - from).ShouldBeLessThanOrEqualTo(TimeSpan.FromDays(90));
        }
    }

    [Fact]
    public async Task A_refused_token_ends_the_phase_with_data_dhan_not_connected_and_the_token_is_nowhere_in_the_job_or_the_logs()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { abb, tcs } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        Factory.Brokers.Override(ChartPath, request => FakeBrokers.Json(HttpStatusCode.Unauthorized, $$"""{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"token {{request.Header("access-token")}} is invalid"}"""));

        var job = await RunAsync(["dhan", "aggregate"]);

        (job.Status, job.Dhan.Status, job.Dhan.Code).ShouldBe(("failed", "failed", "data.dhan_not_connected"));
        Factory.Brokers.Requests.Count(r => r.Uri.AbsolutePath == ChartPath).ShouldBe(1); // the second instrument is not asked
        var json = JsonSerializer.Serialize(job);
        json.ShouldNotContain(AccountsApi.Marker);
        json.ShouldNotContain(Token);
        Factory.Logs.All.ShouldAllBe(line => !line.Contains(AccountsApi.Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_instrument_failing_on_the_wire_is_recorded_with_a_safe_message_and_the_other_still_loads()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { abb, tcs } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var good = BarFixtures.Session(new DateOnly(2026, 10, 12), 3, count: 10);
        var calls = 0;
        Factory.Brokers.Override(ChartPath, request => ++calls == 1
            ? FakeBrokers.Json(HttpStatusCode.BadRequest, $$"""{"errorCode":"DH-905","errorMessage":"leak {{AccountsApi.Marker}}"}""")
            : FakeBrokers.Json(HttpStatusCode.OK, Arrays(request.Body!.Contains("2026-10-12 12:00:00", StringComparison.Ordinal) ? good : []))); // only the window up to now has today's bars

        var job = await RunAsync(["dhan"]);

        (job.Status, job.Dhan.Status, job.Dhan.InstrumentsFetched, job.Dhan.InstrumentsFailed).ShouldBe(("succeeded", "succeeded", 1, 1));
        job.Dhan.Errors.ShouldHaveSingleItem().ShouldContain("NSE:ABB: window ");
        job.Dhan.Errors[0].ShouldContain("Dhan answered HTTP 400, DH-905");
        JsonSerializer.Serialize(job).ShouldNotContain(AccountsApi.Marker);
        (await StoredBarsAsync(tcs)).ShouldBe(good);
        (await StoredBarsAsync(abb)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Timestamps_off_by_the_india_utc_offset_fail_the_instrument_naming_the_shift_and_nothing_is_stored()
    {
        var abb = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { abb } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var shifted = BarFixtures.Session(LastSession, 2).Select(b => b with { EpochSeconds = b.EpochSeconds + (330 * 60) }).ToList(); // India time read as UTC
        Factory.Brokers.Override(ChartPath, request => FakeBrokers.Json(HttpStatusCode.OK, Arrays(request.Body!.Contains("2026-10-12 12:00:00", StringComparison.Ordinal) ? shifted : [])));

        var job = await RunAsync(["dhan"]);

        (job.Status, job.Dhan.Status).ShouldBe(("failed", "failed"));
        job.Dhan.Errors.ShouldHaveSingleItem().ShouldContain("subtracting 5 h 30 min");
        job.Dhan.Errors[0].ShouldContain("nothing was shifted or stored");
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m")).ShouldBe(0);
    }

    [Fact]
    public async Task An_unfaked_dhan_endpoint_means_a_failed_instrument_not_a_crash()
    {
        var abb = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { abb } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var job = await RunAsync(["dhan"]); // the fake Dhan server answers 404 for the chart path unless a test fakes it

        (job.Status, job.Dhan.Status).ShouldBe(("failed", "failed"));
        job.Dhan.Errors.ShouldHaveSingleItem().ShouldContain("HTTP 404");
    }
}
