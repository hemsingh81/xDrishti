using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P10-06 on a real database: the endpoint answers with the five criteria, each one of the three words, the limits always printed, the text, and 404 for an unknown epoch and 422 for a date after the newest closed session. It is a GET and writes nothing.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperPhaseCheckApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string[] Words = ["MET", "NOT MET", "NOT EVALUABLE"];

    [Fact]
    public async Task The_read_returns_five_criteria_with_a_verdict_each_the_limits_and_the_text_and_never_reaches_met_without_data()
    {
        await ResignInAsync();

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/phase-check", UriKind.Relative), Ct);

        var report = view.GetProperty("report");
        report.GetProperty("criteria").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ShouldBe(["C1", "C2", "C3", "C4", "I"]);
        report.GetProperty("criteria").EnumerateArray().ShouldAllBe(c => Words.Contains(c.GetProperty("verdict").GetString()));
        report.GetProperty("exitVerdict").GetString().ShouldNotBe("MET");
        report.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        report.GetProperty("printed").GetProperty("notJudged").GetArrayLength().ShouldBe(5);
        view.GetProperty("text").GetString()!.ShouldContain("limit: Simulated fills on official 1-minute bars");
    }

    [Fact]
    public async Task An_unknown_epoch_is_404_and_a_future_date_or_a_bad_id_is_422()
    {
        await ResignInAsync();

        (await Client.GetAsync(new Uri("/api/paper/phase-check?epoch=999999999", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync(new Uri("/api/paper/phase-check?through=2099-01-01", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Client.GetAsync(new Uri("/api/paper/phase-check?epoch=0", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
