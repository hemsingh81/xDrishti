using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>P15-02 criterion 11 end to end: the three read routes need a session, answer each state, carry metadata and counts only, and refuse a bad query with <c>filings.invalid_query</c> or an unknown id with <c>filings.not_found</c>.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class FilingsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string[] AllowedNames =
    [
        "from", "to", "trackedInstruments", "kinds", "kind", "state", "instrumentsWithout", "quarters", "quarterEnd", "ok", "unavailable", "reason", "count", "superseded", "outOfOrder",
        "unavailableCount", "items", "truncated", "id", "symbol", "periodEnd", "broadcastTs", "broadcastState", "unavailableReason", "ingestedAt", "contentSha256", "fileBytes", "fileName", "supersedes",
    ];

    private Guid _reliance;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _reliance = await SeedAsync("RELIANCE");
    }

    private async Task StoreAsync(NewFiling filing)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFilingStore>().InsertAsync(filing, Ct);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static IEnumerable<string> Names(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var inner in Names(property.Value))
                {
                    yield return inner;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().SelectMany(Names))
            {
                yield return item;
            }
        }
    }

    [Theory]
    [InlineData("/api/filings/coverage")]
    [InlineData("/api/filings")]
    [InlineData("/api/filings/00000000-0000-0000-0000-000000000001")]
    public async Task Without_a_session_the_routes_are_401(string path)
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nothing_stored_is_an_empty_list_and_every_kind_of_the_coverage_answers_unavailable_and_not_zero()
    {
        var (listStatus, list) = await GetAsync("/api/filings");
        var (coverageStatus, coverage) = await GetAsync("/api/filings/coverage");

        listStatus.ShouldBe(HttpStatusCode.OK);
        list.GetProperty("items").GetArrayLength().ShouldBe(0);
        coverageStatus.ShouldBe(HttpStatusCode.OK);
        coverage.GetProperty("kinds").EnumerateArray().Select(k => (k.GetProperty("kind").GetString(), k.GetProperty("state").GetString())).ShouldBe(
            [("results", "unavailable"), ("shareholding", "unavailable"), ("board_meeting", "unavailable"), ("announcement", "unavailable")]);
        coverage.GetProperty("trackedInstruments").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_list_is_newest_broadcast_first_carries_metadata_only_and_filters_by_instrument_and_kind()
    {
        var tcs = await SeedAsync("TCS");
        await StoreAsync(FilingTestKit.Ok(_reliance, "q4", new DateOnly(2026, 3, 31), FilingTestKit.Broadcast.AddDays(-90)));
        await StoreAsync(FilingTestKit.Ok(_reliance, "q1", FilingTestKit.Q1, FilingTestKit.Broadcast));
        await StoreAsync(FilingTestKit.Ok(_reliance, "undated", new DateOnly(2025, 12, 31), null));
        await StoreAsync(FilingTestKit.Ok(_reliance, "sh", FilingTestKit.Q1, FilingTestKit.Broadcast, FilingKind.Shareholding));
        await StoreAsync(FilingTestKit.Ok(tcs, "tcs", FilingTestKit.Q1, FilingTestKit.Broadcast));

        var (status, body) = await GetAsync("/api/filings?instrument=RELIANCE&kind=results");

        status.ShouldBe(HttpStatusCode.OK);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("periodEnd").GetString() + "/" + i.GetProperty("broadcastState").GetString()).ShouldBe(["2026-06-30/ok", "2026-03-31/ok", "2025-12-31/unavailable"]);
        items.ShouldAllBe(i => i.GetProperty("symbol").GetString() == "NSE:RELIANCE");
        items[2].GetProperty("unavailableReason").GetString().ShouldBe("date_only");
        Names(body).Distinct().ShouldBeSubsetOf(AllowedNames);
        (await GetAsync("/api/filings")).Body.GetProperty("items").GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task AsOf_applies_the_point_in_time_rule_in_each_of_its_three_states()
    {
        await StoreAsync(FilingTestKit.Ok(_reliance, "q1", FilingTestKit.Q1, FilingTestKit.Broadcast));
        await StoreAsync(FilingTestKit.Ok(_reliance, "restated", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(37)));
        await StoreAsync(FilingTestKit.Ok(_reliance, "sh-undated", FilingTestKit.Q1, null, FilingKind.Shareholding));
        var ist = "%2B05:30";

        var justBefore = (await GetAsync($"/api/filings?instrument=RELIANCE&kind=results&asOf=2026-07-24T16:16:59{ist}")).Body;
        var at = (await GetAsync($"/api/filings?instrument=RELIANCE&kind=results&asOf=2026-07-24T16:17:00{ist}")).Body;
        var restated = (await GetAsync("/api/filings?instrument=RELIANCE&kind=results&asOf=2026-09-30T00:00:00Z")).Body;
        var unavailable = (await GetAsync("/api/filings?instrument=RELIANCE&kind=shareholding&asOf=2026-09-30T00:00:00Z")).Body;

        (justBefore.GetProperty("state").GetString(), justBefore.GetProperty("unavailableCount").GetInt32(), justBefore.GetProperty("items").GetArrayLength()).ShouldBe(("none_visible", 0, 0));
        at.GetProperty("state").GetString().ShouldBe("value");
        at.GetProperty("items").EnumerateArray().Single().GetProperty("fileBytes").GetInt64().ShouldBe(2);
        restated.GetProperty("items").EnumerateArray().Single().GetProperty("fileBytes").GetInt64().ShouldBe(8);
        (unavailable.GetProperty("state").GetString(), unavailable.GetProperty("items").GetArrayLength()).ShouldBe(("unavailable", 0));
    }

    [Theory]
    [InlineData("/api/filings?kind=memo")]
    [InlineData("/api/filings?instrument=NOSUCH")]
    [InlineData("/api/filings?instrument=RELIANCE&kind=results&asOf=2026-07-24T16:17:00")]
    [InlineData("/api/filings?instrument=RELIANCE&kind=results&asOf=2026-07-24")]
    [InlineData("/api/filings?asOf=2026-07-24T16:17:00Z")]
    [InlineData("/api/filings/coverage?from=2026-01-10&to=2026-01-01")]
    [InlineData("/api/filings/coverage?from=2020-01-01&to=2026-01-01")]
    public async Task A_bad_query_is_400_filings_invalid_query(string path)
    {
        var (status, body) = await GetAsync(path);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().ShouldBe("filings.invalid_query");
    }

    [Fact]
    public async Task One_filing_is_read_by_id_and_an_unknown_id_is_404_filings_not_found()
    {
        var filing = FilingTestKit.Ok(_reliance, "q1", FilingTestKit.Q1, FilingTestKit.Broadcast);
        await StoreAsync(filing);

        var (status, body) = await GetAsync("/api/filings/" + filing.Id);
        var (missing, problem) = await GetAsync("/api/filings/" + Guid.NewGuid());

        status.ShouldBe(HttpStatusCode.OK);
        (body.GetProperty("id").GetGuid(), body.GetProperty("contentSha256").GetString()).ShouldBe((filing.Id, filing.ContentSha256));
        Names(body).Distinct().ShouldBeSubsetOf(AllowedNames);
        missing.ShouldBe(HttpStatusCode.NotFound);
        problem.GetProperty("code").GetString().ShouldBe("filings.not_found");
    }

    [Fact]
    public async Task The_coverage_counts_stored_unavailable_by_reason_superseded_out_of_order_and_the_instruments_with_none()
    {
        var tcs = await SeedAsync("TCS");
        await StoreAsync(FilingTestKit.Ok(_reliance, "q1", FilingTestKit.Q1, FilingTestKit.Broadcast));
        await StoreAsync(FilingTestKit.Ok(_reliance, "restated", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(37)));
        await StoreAsync(FilingTestKit.Ok(_reliance, "early", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(-3)));
        await StoreAsync(FilingTestKit.Ok(_reliance, "undated", FilingTestKit.Q1, null));
        _ = tcs;

        var (status, body) = await GetAsync("/api/filings/coverage?from=2026-01-01&to=2026-12-31");

        status.ShouldBe(HttpStatusCode.OK);
        var results = body.GetProperty("kinds").EnumerateArray().Single(k => k.GetProperty("kind").GetString() == "results");
        var quarter = results.GetProperty("quarters").EnumerateArray().Single();
        quarter.GetProperty("quarterEnd").GetString().ShouldBe("2026-06-30");
        (quarter.GetProperty("ok").GetInt32(), quarter.GetProperty("superseded").GetInt32(), quarter.GetProperty("outOfOrder").GetInt32()).ShouldBe((3, 1, 1));
        quarter.GetProperty("unavailable").EnumerateArray().Select(u => (u.GetProperty("reason").GetString(), u.GetProperty("count").GetInt32())).ShouldBe([("date_only", 1)]);
        (results.GetProperty("state").GetString(), results.GetProperty("instrumentsWithout").GetInt32()).ShouldBe(("value", 1));
        Names(body).Distinct().ShouldBeSubsetOf(AllowedNames);
    }
}
