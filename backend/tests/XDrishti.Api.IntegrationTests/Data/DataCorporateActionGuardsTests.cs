using System.Net;
using System.Text;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Actions;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The guards around applying corporate actions against a real TimescaleDB (ADR 0017 §5): the owner's CSV files are adjusted at source, so an action inside the part they cover needs consent
/// (<c>data.csv_already_adjusted</c>); a split and a bonus on one day do not both apply (<c>data.corporate_action_conflicts_same_day</c>); the detection does not bring back a date the owner moved away from.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCorporateActionGuardsTests(ApiFactory factory) : DataTestBase(factory)
{
    private async Task<(Guid Id, List<SeriesDay> Series)> LoadAsync(string ticker, List<SeriesDay> series)
    {
        var id = await SeedAsync(ticker);
        WriteCsv(ticker, ActionFixtures.Bars(series));
        return (id, series);
    }

    private async Task<CorporateActionPage> ListAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/corporate-actions" + query, Ct);
        return await response.ReadAsync<CorporateActionPage>(Ct);
    }

    private async Task<HttpResponseMessage> AddAsync(Guid instrument, DateOnly exDate, string kind = "split", int numerator = 1, int denominator = 5, string query = "", bool? bodyAck = null) =>
        await Client.PostJsonAsync(
            "/api/data/corporate-actions" + query,
            bodyAck is null
                ? new { instrumentId = instrument, exDate = exDate.ToString("yyyy-MM-dd"), kind, numerator, denominator }
                : (object)new { instrumentId = instrument, exDate = exDate.ToString("yyyy-MM-dd"), kind, numerator, denominator, acknowledgeCsvAdjusted = bodyAck },
            Ct);

    private async Task<HttpResponseMessage> ImportAsync(string text, string query) =>
        await Client.PostAsync(new Uri("/api/data/corporate-actions/import" + query, UriKind.Relative), new StringContent(text, Encoding.UTF8, "text/csv"), Ct);

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task The_list_says_how_far_the_loaded_files_reach_and_an_action_inside_that_part_is_refused_until_acknowledged_in_the_query_or_the_body()
    {
        var (abb, series) = await LoadAsync("ABB", ActionFixtures.Series());
        await SeedAsync("TCS");
        await RunAsync(["csv", "aggregate"]);
        var through = series[^1].Date;
        using var refused = await AddAsync(abb, series[20].Date);
        var problem = await ProblemAsync(refused, Ct);
        using var viaQuery = await AddAsync(abb, series[20].Date, query: "?acknowledgeCsvAdjusted=true");
        using var viaBody = await AddAsync(abb, series[25].Date, bodyAck: true);
        using var afterTheFiles = await AddAsync(abb, through.AddDays(10));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (problem.GetProperty("code").GetString(), problem.GetProperty("adjustsCsvTwice").GetInt32(), problem.GetProperty("instruments").GetInt32()).ShouldBe(("data.csv_already_adjusted", 1, 1));
        problem.GetProperty("title").GetString()!.ShouldContain(through.ToString("yyyy-MM-dd"));
        viaQuery.StatusCode.ShouldBe(HttpStatusCode.Created);
        viaBody.StatusCode.ShouldBe(HttpStatusCode.Created);
        afterTheFiles.StatusCode.ShouldBe(HttpStatusCode.Created);
        var page = await ListAsync();
        page.Total.ShouldBe(3);
        page.Items.ShouldAllBe(i => i.CsvAdjustedThrough == through);
        (await viaQuery.ReadAsCreatedAsync<CorporateActionView>(Ct)).CsvAdjustedThrough.ShouldBe(through);
    }

    [Fact]
    public async Task An_instrument_without_loaded_files_has_no_date_and_no_guard_and_undoing_the_load_lifts_it()
    {
        var (abb, series) = await LoadAsync("ABB", ActionFixtures.Series(days: 10));
        var tcs = await SeedAsync("TCS");
        await RunAsync(["csv", "aggregate"]);
        using (var tcsAction = await AddAsync(tcs, series[3].Date))
        {
            tcsAction.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await tcsAction.ReadAsCreatedAsync<CorporateActionView>(Ct)).CsvAdjustedThrough.ShouldBeNull();
        }

        using (var refused = await AddAsync(abb, series[3].Date))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        using var undo = await Client.PostAsync("/api/data/csv/files/ABB_1m.csv.gz/undo", Ct);
        undo.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        using var allowed = await AddAsync(abb, series[3].Date);

        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await allowed.ReadAsCreatedAsync<CorporateActionView>(Ct)).CsvAdjustedThrough.ShouldBeNull();
    }

    [Fact]
    public async Task Confirming_a_detected_suggestion_inside_the_loaded_range_needs_the_acknowledgement()
    {
        await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5));
        await RunAsync();
        var id = (await ListAsync()).Items.Single().Id;

        using var refused = await Client.PutJsonAsync($"/api/data/corporate-actions/{id}", new { }, Ct);
        var stillSuggested = (await ListAsync()).Items.Single().Status;
        using var accepted = await Client.PutJsonAsync($"/api/data/corporate-actions/{id}", new { acknowledgeCsvAdjusted = true }, Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused, Ct)).GetProperty("code").GetString().ShouldBe("data.csv_already_adjusted");
        stillSuggested.ShouldBe("suggested");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.ReadAsync<CorporateActionView>(Ct)).Status.ShouldBe("applied");
    }

    [Fact]
    public async Task The_import_preview_lists_the_lines_per_line_and_the_import_needs_the_acknowledgement_for_them()
    {
        var (abb, series) = await LoadAsync("ABB", ActionFixtures.Series());
        await SeedAsync("TCS");
        await RunAsync(["csv", "aggregate"]);
        var inside = series[10].Date.ToString("yyyy-MM-dd");
        var after = series[^1].Date.AddDays(30).ToString("yyyy-MM-dd");
        var text = $"symbol,exDate,kind,numerator,denominator\nABB,{inside},split,1,5\nABB,{after},split,1,2\nTCS,{inside},bonus,1,1\n";

        using var preview = await ImportAsync(text, "?preview=true");
        var previewed = await preview.ReadAsync<ImportCorporateActionsResult>(Ct);
        using var refused = await ImportAsync(text, string.Empty);
        var storedAfterRefusal = (await ListAsync()).Total;
        using var accepted = await ImportAsync(text, "?acknowledgeCsvAdjusted=true");

        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        (previewed.Importable, previewed.AdjustsCsvTwice).ShouldBe((3, 1));
        var warning = previewed.Warnings.ShouldHaveSingleItem();
        (warning.Line, warning.Symbol, warning.ExDate, warning.CsvAdjustedThrough).ShouldBe((2, "NSE:ABB", series[10].Date, series[^1].Date));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ProblemAsync(refused, Ct);
        (problem.GetProperty("code").GetString(), problem.GetProperty("adjustsCsvTwice").GetInt32(), problem.GetProperty("instruments").GetInt32()).ShouldBe(("data.csv_already_adjusted", 1, 1));
        storedAfterRefusal.ShouldBe(0);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.ReadAsync<ImportCorporateActionsResult>(Ct)).Imported.ShouldBe(3);
        (await ListAsync()).Items.Single(i => i.InstrumentId == abb && i.ExDate == series[10].Date).CsvAdjustedThrough.ShouldBe(series[^1].Date);
    }

    [Fact]
    public async Task A_split_and_a_bonus_on_one_day_do_not_both_apply_and_a_pending_guess_of_the_other_kind_is_declined_by_itself()
    {
        var (abb, series) = await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5));
        await RunAsync();
        var exDate = series[ActionFixtures.EventDay].Date;
        var guess = (await ListAsync()).Items.Single(); // the detection's split 1:5 on the event day

        using var bonus = await AddAsync(abb, exDate, "bonus", 4, 1, query: "?acknowledgeCsvAdjusted=true"); // the owner knows better: a bonus
        using var splitAgain = await AddAsync(abb, exDate, "split", 1, 5, query: "?acknowledgeCsvAdjusted=true");

        bonus.StatusCode.ShouldBe(HttpStatusCode.Created);
        var declined = (await ListAsync("?status=ignored")).Items.ShouldHaveSingleItem();
        (declined.Id, declined.Kind).ShouldBe((guess.Id, "split"));
        splitAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict); // the declined one exists (any status)
        (await ProblemAsync(splitAgain, Ct)).GetProperty("code").GetString().ShouldBe("data.corporate_action_exists");
        using var another = await AddAsync(abb, exDate, "split", 1, 2, query: "?acknowledgeCsvAdjusted=true");
        another.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(another, Ct)).GetProperty("code").GetString().ShouldBe("data.corporate_action_exists");
        (await OverviewAsync()).PendingCorporateActions.ShouldBe(0);
    }

    [Fact]
    public async Task An_applied_action_blocks_the_other_kind_on_its_day_until_it_is_ignored()
    {
        var abb = await SeedAsync("ABB");
        var day = new DateOnly(2026, 9, 1);
        (await AddAsync(abb, day, "bonus", 1, 1)).EnsureSuccessStatusCode();

        using var blocked = await AddAsync(abb, day, "split", 1, 5);
        var bonusId = (await ListAsync()).Items.Single().Id;
        (await Client.PostAsync($"/api/data/corporate-actions/{bonusId}/ignore", Ct)).EnsureSuccessStatusCode();
        using var allowed = await AddAsync(abb, day, "split", 1, 5);

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(blocked, Ct)).GetProperty("code").GetString().ShouldBe("data.corporate_action_conflicts_same_day");
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_import_refuses_a_line_against_an_applied_action_of_the_other_kind_as_a_problem_line_and_imports_the_rest()
    {
        var abb = await SeedAsync("ABB");
        (await AddAsync(abb, new DateOnly(2026, 9, 1), "bonus", 1, 1)).EnsureSuccessStatusCode();

        using var response = await ImportAsync("symbol,exDate,kind,numerator,denominator\nABB,2026-09-01,split,1,5\nABB,2026-09-02,split,1,2\n", string.Empty);
        var result = await response.ReadAsync<ImportCorporateActionsResult>(Ct);

        (result.Imported, result.Invalid).ShouldBe((1, 1));
        result.Errors.ShouldHaveSingleItem().Line.ShouldBe(2);
    }

    [Fact]
    public async Task A_suggestion_the_owner_moved_to_another_ex_date_is_not_suggested_again_by_a_later_detection_or_a_full_rebuild()
    {
        var (_, series) = await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5));
        await RunAsync();
        var suggestion = (await ListAsync()).Items.Single();
        var moved = series[ActionFixtures.EventDay + 1].Date;
        using var edit = await Client.PutJsonAsync($"/api/data/corporate-actions/{suggestion.Id}", new { exDate = moved.ToString("yyyy-MM-dd"), acknowledgeCsvAdjusted = true }, Ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var detect = await Client.PostAsync("/api/data/corporate-actions/detect", Ct);
        var rebuild = await RunAsync(["aggregate"]);

        (await detect.ReadAsync<DetectionResult>(Ct)).Suggested.ShouldBe(0);
        rebuild.Aggregate.Message.ShouldBeNull();
        var page = await ListAsync();
        var only = page.Items.ShouldHaveSingleItem();
        (only.Id, only.ExDate, only.Status).ShouldBe((suggestion.Id, moved, "applied"));
        (await ScalarAsync<DateOnly>("SELECT detected_ex_date FROM market.corporate_actions WHERE id = @id", ("id", suggestion.Id))).ShouldBe(series[ActionFixtures.EventDay].Date);
    }
}
