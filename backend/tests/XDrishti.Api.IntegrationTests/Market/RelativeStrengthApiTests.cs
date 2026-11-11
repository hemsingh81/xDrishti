using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Strength;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-09 on a real database: the rows are written once per day (a second write inserts nothing), read back newest first through the endpoint, and a bad parameter is a 400 problem.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class RelativeStrengthApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    private static readonly Guid Basket = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    [Fact]
    public async Task A_day_is_written_once_and_read_back_through_the_endpoint_without_the_index_relative_rows()
    {
        await ImportAsync();
        var ids = await SharesAsync(2);
        StrengthRow[] rows =
        [
            new(Day, ids[0], Basket, 126, 21, 3.2, 1.0, 12, "value", "single_snapshot"),
            new(Day, ids[1], Basket, 126, 21, null, null, 0, "corporate_action_unresolved", "single_snapshot"),
            new(Day, ids[0], Guid.Empty, 20, 0, 2.5, null, 0, "value", "none"),
        ];
        SectorRow[] sectors = [new(Day, "Banks", 63, 0.03, 1, 6, "value")];
        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRelativeStrengthStore>();
            (await store.InsertDayAsync(Day, rows, sectors, Ct)).ShouldBeTrue();
            (await store.InsertDayAsync(Day, rows, sectors, Ct)).ShouldBeFalse("a day that has rows is never rewritten");
            (await store.DatesWithRowsAsync(Day, Day, Ct)).ShouldBe([Day]);
        }

        using var response = await Client.GetAsync(new Uri($"/api/market/relative-strength?basket={Basket}&asOf=2026-10-09", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        var items = body.GetProperty("items");
        items.GetArrayLength().ShouldBe(2);
        items[0].GetProperty("pctRank").GetDouble().ShouldBe(1.0);
        items[0].GetProperty("membershipBasis").GetString().ShouldBe("single_snapshot");
        items[1].GetProperty("state").GetString().ShouldBe("corporate_action_unresolved");
        items[1].GetProperty("score").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("cap").GetInt32().ShouldBe(500);
    }

    [Theory]
    [InlineData("asOf=yesterday")]
    [InlineData("basket=Nifty%20999")]
    public async Task A_bad_parameter_is_a_problem_that_says_what_is_allowed(string query)
    {
        using var response = await Client.GetAsync(new Uri("/api/market/relative-strength?" + query, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("market.relative_strength.invalid");
    }

    [Fact]
    public async Task Without_a_session_the_endpoint_is_refused()
    {
        using var anonymous = Factory.CreateAnonymousClient();

        using var response = await anonymous.GetAsync(new Uri("/api/market/relative-strength", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
