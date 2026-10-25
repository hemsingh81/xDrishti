using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P8-10 end to end: the excursion read over the stored fixture (<see cref="ReportFixture"/>). Every fabricated trade has MAE 0.4 R, MFE 2.1 R and enters at 10:00 India time; RPTA has 62 trades (32 wins of +1 R, 30 losses of -1 R) and is intraday, RPTB has 31 and is swing.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportMaeMfeApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string RunA = ReportFixture.RunA.RunKey;

    private static readonly string Both = ReportFixture.RunA.RunKey + "," + ReportFixture.RunB.RunKey;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ReportFixture.SeedAsync(Factory, Ct);
    }

    private async Task<JsonElement> GetOkAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static int[] Ints(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetInt32())];

    [Fact]
    public async Task The_excursion_counts_of_a_run_are_the_hand_worked_bins()
    {
        var view = await GetOkAsync($"/api/reports/mae-mfe?runs={RunA}");

        var group = view.GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("n").GetInt32().ShouldBe(62);
        group.GetProperty("excursionN").GetInt32().ShouldBe(62);
        var grid = group.GetProperty("grid");
        var counts = grid.GetProperty("counts").EnumerateArray().Select(Ints).ToArray();
        counts.Length.ShouldBe(9);
        counts.ShouldAllBe(row => row.Length == 22);
        counts.Sum(r => r.Sum()).ShouldBe(62);
        counts[1][9].ShouldBe(30, "MAE 0.4 R is in [0.25, 0.5); a loss of -1 R is in [-1, -0.5)");
        counts[1][13].ShouldBe(32, "a win of +1 R is in [1, 1.5)");
        grid.GetProperty("belowCount").GetInt32().ShouldBe(0);
        grid.GetProperty("aboveCount").GetInt32().ShouldBe(0);
        var mfe = group.GetProperty("mfe");
        Ints(mfe.GetProperty("counts"))[4].ShouldBe(62, "MFE 2.1 R is in [2, 2.5)");
        mfe.GetProperty("aboveCount").GetInt32().ShouldBe(0);
        var reached = group.GetProperty("reachedMfe").EnumerateArray().Select(l => (l.GetProperty("atR").GetDouble(), l.GetProperty("count").GetInt32())).ToArray();
        reached.ShouldBe([(1.0, 62), (2.0, 62), (3.0, 0)]);
        group.GetProperty("winnersMaeP90").GetProperty("number").GetDouble().ShouldBe(0.4, 1e-12);
        view.GetProperty("evidence").GetProperty("n").GetInt32().ShouldBe(62);
    }

    [Fact]
    public async Task The_hour_lens_has_the_india_hour_for_the_intraday_run_and_nothing_for_the_swing_run()
    {
        var view = await GetOkAsync($"/api/reports/mae-mfe?runs={Both}&lens=hour");

        var groups = view.GetProperty("groups").EnumerateArray().ToArray();
        groups.Length.ShouldBe(2);
        var ten = groups.Single(g => g.GetProperty("parts")[0].GetProperty("value").ValueKind == JsonValueKind.String);
        ten.GetProperty("parts")[0].GetProperty("value").GetString().ShouldBe("10");
        ten.GetProperty("n").GetInt32().ShouldBe(62);
        var none = groups.Single(g => g.GetProperty("parts")[0].GetProperty("value").ValueKind == JsonValueKind.Null);
        none.GetProperty("n").GetInt32().ShouldBe(31);
        none.TryGetProperty("grid", out var grid).ShouldBeTrue();
        grid.ValueKind.ShouldBe(JsonValueKind.Null);
        none.GetProperty("winnersMaeP90").GetProperty("state").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public async Task The_sector_lens_leaves_an_instrument_the_master_does_not_know_not_recorded()
    {
        var view = await GetOkAsync($"/api/reports/groups?runs={RunA}&lens=sector");

        var group = view.GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("n").GetInt32().ShouldBe(62);
        group.GetProperty("parts")[0].GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        group.GetProperty("expectancyR").GetProperty("state").GetString().ShouldBe("unavailable");
        group.GetProperty("expectancyR").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_endpoint_needs_a_signed_in_owner_and_refuses_an_unknown_lens()
    {
        using var anonymous = Factory.CreateClient();
        using var denied = await anonymous.GetAsync(new Uri("/api/reports/mae-mfe", UriKind.Relative), Ct);
        using var unknown = await Client.GetAsync(new Uri($"/api/reports/mae-mfe?runs={RunA}&lens=colour", UriKind.Relative), Ct);

        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
