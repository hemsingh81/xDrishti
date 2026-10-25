using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P8-10 part b end to end over the stored fixture (<see cref="ReportFixture"/>): the stored comparisons, the report over their members split by exit, and the stored walk-forward exit choice read from its record.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportComparisonsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string ComparisonKey = StoreKit.Sha256("report-fixture-comparison");

    private static readonly string SelectionKey = StoreKit.Sha256("report-fixture-selection");

    private static readonly string BadKey = StoreKit.Sha256("report-fixture-selection-bad");

    private const string Record = """
        {"format":"x","schema":1,"cell":"RPTA 15m BUY intraday *","unit":"RFilled","default":"FIXED_RR{rr=2}","chosen":"FIXED_RR{rr=3}","no_choice_reason":null,
         "folds":[
           {"index":1,"test_start":"2025-01-01","test_end":"2025-03-31","winner":"FIXED_RR{rr=3}","winner_test_trades":40,"winner_test_score":0.250000,"thin":false},
           {"index":2,"test_start":"2025-04-01","test_end":"2025-06-30","winner":"FIXED_RR{rr=2}","winner_test_trades":12,"winner_test_score":0.100000,"thin":true},
           {"index":3,"test_start":"2025-07-01","test_end":"2025-09-30","winner":null,"winner_test_trades":0,"winner_test_score":null,"thin":true}],
         "pooled":[{"name":"walk-forward choice","trades":52,"mean_r":0.2,"score":0.15},{"name":"default FIXED_RR{rr=2}","trades":52,"mean_r":null,"score":null}],
         "stability":{"stable":false,"reason":"only 1 usable fold","folds":1,"modal":"FIXED_RR{rr=3}","agreeing":1,"positive_out_of_sample":1,"chance_pass":0.333333,"lone_peaks":0},
         "banner":["The choice is in-sample on the data shown and is not evidence of an edge.","1 folds only."]}
        """;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ReportFixture.SeedAsync(Factory, Ct);
        await using var admin = new NpgsqlConnection(Factory.AdminConnectionString);
        await admin.OpenAsync(Ct);
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO research.exit_selections (selection_key, comparison_id, strategy_id, timeframe, side, style, regime, chosen_candidate_id, stable, record_json, record_sha256, created_at, created_by)
            SELECT @key, id, 'RPTA', '15m', 'BUY', 'intraday', '*', 'FIXED_RR{rr=3}', false, @json, @sha, now(), 'test' FROM research.sim_comparisons WHERE comparison_key = @comparison
            ON CONFLICT (selection_key) DO NOTHING
            """,
            admin);
        insert.Parameters.AddWithValue("key", NpgsqlDbType.Char, SelectionKey);
        insert.Parameters.AddWithValue("comparison", NpgsqlDbType.Char, ComparisonKey);
        insert.Parameters.AddWithValue("json", NpgsqlDbType.Text, Record);
        insert.Parameters.AddWithValue("sha", NpgsqlDbType.Char, StoreKit.Sha256(Record));
        await insert.ExecuteNonQueryAsync(Ct);
        insert.Parameters["key"].Value = BadKey;
        insert.Parameters["json"].Value = "{not json";
        insert.Parameters["sha"].Value = StoreKit.Sha256("{not json");
        await insert.ExecuteNonQueryAsync(Ct);
    }

    private async Task<JsonElement> GetOkAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task A_stored_comparison_is_listed_with_its_members_and_candidate_ids()
    {
        var view = await GetOkAsync("/api/reports/comparisons");

        var item = view.GetProperty("comparisons").EnumerateArray().Single(c => c.GetProperty("comparisonKey").GetString() == ComparisonKey);
        item.GetProperty("strategyId").GetString().ShouldBe("RPTA");
        item.GetProperty("unit").GetString().ShouldBe("RFilled");
        item.GetProperty("commonEntries").GetInt32().ShouldBe(1);
        item.GetProperty("members").EnumerateArray().Select(m => (m.GetProperty("runKey").GetString(), m.GetProperty("candidateId").GetString()))
            .ShouldBe([(ReportFixture.RunA.RunKey, "FIXED_RR{rr=2}"), (ReportFixture.RunB.RunKey, "FIXED_RR{rr=3}")]);
        view.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_members_of_a_comparison_report_by_exit_with_the_same_n_in_both_rows()
    {
        var runs = $"{ReportFixture.RunA.RunKey},{ReportFixture.RunAOtherExit.RunKey}";

        var groups = await GetOkAsync($"/api/reports/groups?runs={runs}&lens=exit");

        var rows = groups.GetProperty("groups").EnumerateArray().ToArray();
        rows.Length.ShouldBe(2);
        rows.Select(g => g.GetProperty("n").GetInt32()).Distinct().ShouldBe([62]);
        rows.Select(g => g.GetProperty("parts")[0].GetProperty("value").GetString()).Order(StringComparer.Ordinal).ShouldBe(["FIXED_RR{rr=2}", "FIXED_RR{rr=3}"]);
    }

    [Fact]
    public async Task The_stored_exit_choice_is_read_from_its_record_with_non_values_as_states()
    {
        var view = await GetOkAsync("/api/reports/exit-selections");

        var s = view.GetProperty("selections").EnumerateArray().Single(x => x.GetProperty("selectionKey").GetString() == SelectionKey);
        s.GetProperty("comparisonKey").GetString().ShouldBe(ComparisonKey);
        s.GetProperty("chosenCandidateId").GetString().ShouldBe("FIXED_RR{rr=3}");
        s.GetProperty("defaultCandidateId").GetString().ShouldBe("FIXED_RR{rr=2}");
        s.GetProperty("stable").GetBoolean().ShouldBeFalse();
        s.GetProperty("chancePass").GetDouble().ShouldBe(0.333333, 1e-9);
        var pooled = s.GetProperty("pooled").EnumerateArray().ToArray();
        pooled[0].GetProperty("meanR").GetProperty("state").GetString().ShouldBe("value");
        pooled[0].GetProperty("meanR").GetProperty("number").GetDouble().ShouldBe(0.2, 1e-12);
        pooled[1].GetProperty("meanR").GetProperty("state").GetString().ShouldBe("unavailable");
        pooled[1].GetProperty("meanR").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        var folds = s.GetProperty("folds").EnumerateArray().ToArray();
        folds.Length.ShouldBe(3);
        folds[0].GetProperty("winnerTestScore").GetProperty("state").GetString().ShouldBe("value");
        folds[1].GetProperty("winnerTestScore").GetProperty("state").GetString().ShouldBe("insufficient", "12 test trades is below the minimum");
        folds[2].GetProperty("winnerTestScore").GetProperty("state").GetString().ShouldBe("empty");
        s.GetProperty("banner").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_stored_record_that_cannot_be_read_is_listed_as_unreadable_with_no_figure_and_does_not_fail_the_list()
    {
        var view = await GetOkAsync("/api/reports/exit-selections");

        var bad = view.GetProperty("selections").EnumerateArray().Single(x => x.GetProperty("selectionKey").GetString() == BadKey);
        bad.GetProperty("unreadable").GetString().ShouldNotBeNullOrEmpty();
        bad.GetProperty("pooled").GetArrayLength().ShouldBe(0);
        bad.GetProperty("folds").GetArrayLength().ShouldBe(0);
        view.GetProperty("selections").EnumerateArray().Any(x => x.GetProperty("selectionKey").GetString() == SelectionKey).ShouldBeTrue();
    }

    [Fact]
    public async Task Both_endpoints_need_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();

        foreach (var path in new[] { "comparisons", "exit-selections" })
        {
            using var response = await anonymous.GetAsync(new Uri("/api/reports/" + path, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }
    }
}
