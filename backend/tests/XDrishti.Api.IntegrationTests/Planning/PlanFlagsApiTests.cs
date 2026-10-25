using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P8-14 on a real database: the plan's stored flags round-trip (an old plan reads null), and the ticket chart endpoint refuses what it should and says plainly when a symbol has no instrument.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanFlagsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2099, 1, 5);

    private static readonly DateTimeOffset ReviewCutoff = new(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);

    private static readonly string[] LongBanner =
    [
        "market gate unavailable (VIX, breadth not known): the plan is NOT treated as a normal day; judge the market yourself",
        new string('x', 700),
    ];

    private static NewPlanItem Item(string symbol = "NSE:NOSUCHSYMBOL") =>
        new(1, TicketGroup.Primary, symbol, "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", [], new PlanEvidence("cell", 41, "range", "basis", 0.5, 0.1, [], null), []);

    private async Task<long> PublishAsync(string key, int version, PlanBasisFlags? flags, string symbol = "NSE:NOSUCHSYMBOL")
    {
        using var scope = Factory.Services.CreateScope();
        var plan = new NewPlan(key, Session, ReviewCutoff, version, new string('c', 64), "basis", true, "test", DateTimeOffset.UnixEpoch, [Item(symbol)]) { BasisFlags = flags };
        return (await scope.ServiceProvider.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct)).Id;
    }

    [Fact]
    public async Task A_plan_with_flags_returns_them_in_full_and_a_plan_without_returns_null()
    {
        var flags = new PlanBasisFlags(1, "unavailable", ["VIX", "breadth"], "cut", "applied", 3, "assumed_empty", 0.75, true, "costed_unconfirmed", LongBanner);
        var withFlags = await PublishAsync("flags-v1", 1, flags);
        var without = await PublishAsync("flags-v2", 2, null);

        var one = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{withFlags}", UriKind.Relative), Ct);
        var old = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{without}", UriKind.Relative), Ct);
        var list = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans?date=2099-01-05", UriKind.Relative), Ct);

        var f = one.GetProperty("flags");
        f.GetProperty("gate").GetString().ShouldBe("unavailable");
        f.GetProperty("gateMissing").EnumerateArray().Select(e => e.GetString()).ShouldBe(["VIX", "breadth"]);
        f.GetProperty("throttle").GetString().ShouldBe("cut");
        f.GetProperty("correlationUnavailablePairs").GetInt32().ShouldBe(3);
        f.GetProperty("openBook").GetString().ShouldBe("assumed_empty");
        f.GetProperty("riskMultiplier").GetDouble().ShouldBe(0.75);
        f.GetProperty("ruleRemovedHalf").GetBoolean().ShouldBeTrue();
        f.GetProperty("costBasis").GetString().ShouldBe("costed_unconfirmed");
        f.GetProperty("banner")[1].GetString()!.Length.ShouldBe(700);
        old.GetProperty("flags").ValueKind.ShouldBe(JsonValueKind.Null);
        list.EnumerateArray().Single(p => p.GetProperty("id").GetInt64() == withFlags).GetProperty("flags").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public async Task The_chart_is_401_without_a_session_404_for_unknown_ids_422_for_bad_bars_and_not_found_for_a_symbol_with_no_instrument()
    {
        var plan = await PublishAsync("chart-v1", 1, null);
        long item;
        using (var scope = Factory.Services.CreateScope())
        {
            item = (await scope.ServiceProvider.GetRequiredService<IPlanStore>().ItemsAsync(plan, Ct))[0].Id;
        }

        using var anonymous = await Factory.CreateAnonymousClient().GetAsync(new Uri($"/api/plans/{plan}/items/{item}/chart", UriKind.Relative), Ct);
        using var noPlan = await Client.GetAsync(new Uri($"/api/plans/999999/items/{item}/chart", UriKind.Relative), Ct);
        using var noItem = await Client.GetAsync(new Uri($"/api/plans/{plan}/items/999999/chart", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri($"/api/plans/{plan}/items/{item}/chart?bars=5", UriKind.Relative), Ct);
        var ok = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/items/{item}/chart", UriKind.Relative), Ct);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        noPlan.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noItem.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ok.GetProperty("resolution").GetString().ShouldBe("not_found");
        ok.GetProperty("barCount").GetInt32().ShouldBe(0);
        ok.GetProperty("levels").GetProperty("source").GetString().ShouldBe("none");
        ok.GetProperty("rules").GetProperty("trigger").GetString().ShouldBe("t");
    }

    [Fact]
    public async Task The_chart_of_a_resolved_symbol_returns_the_stored_bars_up_to_the_session_and_none_after()
    {
        var id = await SeedAsync("BARTEST");
        await using (var connection = await OpenAsync())
        {
            foreach (var (day, close) in new[] { (new DateOnly(2098, 12, 30), 100.0), (new DateOnly(2098, 12, 31), 101.0), (new DateOnly(2099, 1, 2), 102.0), (new DateOnly(2099, 1, 5), 150.0) })
            {
                await ExecuteAsync(
                    connection,
                    "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, 1000, 375, true)",
                    ("id", id), ("ts", new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime()), ("c", close));
            }
        }

        var plan = await PublishAsync("chart-bars", 3, null, "NSE:BARTEST");
        long item;
        using (var scope = Factory.Services.CreateScope())
        {
            item = (await scope.ServiceProvider.GetRequiredService<IPlanStore>().ItemsAsync(plan, Ct))[0].Id;
        }

        var chart = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/items/{item}/chart", UriKind.Relative), Ct);

        chart.GetProperty("resolution").GetString().ShouldBe("exact");
        chart.GetProperty("session").GetString().ShouldBe("2099-01-02");
        chart.GetProperty("barCount").GetInt32().ShouldBe(3);
        chart.GetProperty("bars").EnumerateArray().Select(b => b.GetProperty("t").GetString()).ShouldBe(["2098-12-30", "2098-12-31", "2099-01-02"]);
        chart.GetProperty("bars").EnumerateArray().Select(b => b.GetProperty("c").GetDouble()).ShouldBe([100.0, 101.0, 102.0]);
    }
}
