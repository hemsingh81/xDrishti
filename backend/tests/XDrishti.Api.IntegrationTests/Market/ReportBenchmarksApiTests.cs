using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P8-07 on a real database: the benchmarks endpoint over real baskets, its refusals, the evidence block and the session guard.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportBenchmarksApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private const string Window = "from=2026-01-05&to=2026-03-31&membersAsOf=2099-01-01";

    [Fact]
    public async Task A_basket_whose_members_have_no_bars_names_them_and_gives_no_number_with_the_evidence()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        var basket = await CreateBasketAsync("Bench");
        await AddAsync(basket.Id, tcs.Id, reliance.Id);

        using var response = await Client.GetAsync(new Uri($"/api/reports/benchmarks?basket={basket.Id}&{Window}", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        var one = body.GetProperty("baskets")[0];
        one.GetProperty("members").GetInt32().ShouldBe(2);
        one.GetProperty("buyAndHold").GetProperty("state").GetString().ShouldBe("empty");
        one.GetProperty("excluded").EnumerateArray().Select(e => e.GetProperty("reason").GetString()).ShouldAllBe(r => r == "no_bars");
        body.GetProperty("nonAdditive").GetBoolean().ShouldBeTrue();
        body.GetProperty("window").GetProperty("source").GetString().ShouldBe("query");
        body.GetProperty("index").GetProperty("return").GetProperty("state").GetString().ShouldBe("unavailable");
        body.GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("price return, dividends not included");
        body.GetProperty("evidence").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Refusals_carry_their_status_and_code()
    {
        var missing = await GetProblemAsync($"/api/reports/benchmarks?basket={Guid.NewGuid()}&{Window}");
        var invalid = await GetProblemAsync($"/api/reports/benchmarks?basket=nope&{Window}");

        missing.ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
        invalid.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
    }

    [Fact]
    public async Task The_endpoint_needs_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/reports/benchmarks", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpStatusCode Status, string Code)> GetProblemAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("code").GetString()!);
    }
}
