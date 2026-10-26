using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P8-06 end to end on a real TimescaleDB: the report endpoints over stored runs, the figures of the hand-worked fixture (<see cref="ReportFixture"/>), the refusals with their status and code, the honest states, the evidence block, the paging of the trade list, and
/// the rule that no request can make a report net.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string Both = ReportFixture.RunA.RunKey + "," + ReportFixture.RunB.RunKey;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ReportFixture.SeedAsync(Factory, Ct);
        await ExecuteSqlAsync("DELETE FROM platform.app_settings WHERE key = 'reports.charge_sheet_confirmation'");
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    /// <summary>
    /// Stores the owner's confirmation of a sheet as the owner's act would (P8-23): the capability row, then the confirmation row (the database stamps the date); the endpoint is covered by <c>ChargeSheetApiTests</c>.
    /// Returns the date the database stamped, as the reports print it.
    /// </summary>
    private async Task<string> ConfirmAsync(string sheetId, string? rates = null)
    {
        await ExecuteSqlAsync($"INSERT INTO workflow.charge_sheet_events (kind, actor, reason) VALUES ('gate_on', 'owner:test@example.com', 'test'); INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('confirm', '{sheetId}', '{rates ?? FixtureSheet.Sheet.RatesSha256}', 'owner:test@example.com', 'test')");
        return await ScalarAsync<string>("SELECT to_char(confirmed_on, 'YYYY-MM-DD') FROM workflow.charge_sheet_events WHERE kind = 'confirm' ORDER BY id DESC LIMIT 1");
    }

    private async Task<JsonElement> GetOkAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<(HttpStatusCode Status, string Code)> GetProblemAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("code").GetString()!);
    }

    private static double Number(JsonElement metric) => metric.GetProperty("number").GetDouble();

    [Fact]
    public async Task The_summary_of_the_two_runs_is_the_hand_worked_total_with_its_evidence()
    {
        var summary = await GetOkAsync($"/api/reports/summary?runs={Both}");

        var all = summary.GetProperty("all");
        all.GetProperty("n").GetInt32().ShouldBe(93);
        all.GetProperty("wins").GetInt32().ShouldBe(48);
        all.GetProperty("winRate").GetProperty("state").GetString().ShouldBe("value");
        Number(all.GetProperty("winRate")).ShouldBe(48.0 / 93, 1e-12);
        Number(all.GetProperty("totalR")).ShouldBe(11, 1e-12);
        Number(all.GetProperty("expectancyR")).ShouldBe(11.0 / 93, 1e-12);
        var evidence = summary.GetProperty("evidence");
        evidence.GetProperty("basis").GetString().ShouldBe("gross");
        evidence.GetProperty("n").GetInt32().ShouldBe(93);
        evidence.GetProperty("defaultRunSet").GetBoolean().ShouldBeFalse();
        evidence.GetProperty("runKeys").EnumerateArray().Select(k => k.GetString()).ShouldBe([ReportFixture.RunA.RunKey, ReportFixture.RunB.RunKey], ignoreOrder: true);
        evidence.GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain(l => l!.Contains("Replayed history", StringComparison.Ordinal));
        evidence.GetProperty("unavailableInputs").GetArrayLength().ShouldBeGreaterThan(0);
        evidence.GetProperty("runs").EnumerateArray().Select(r => r.GetProperty("strategyId").GetString()).ShouldBe(["RPTA", "RPTB"], ignoreOrder: true);
    }

    [Fact]
    public async Task Grouped_by_basket_the_three_baskets_have_their_totals_and_the_report_says_they_are_not_additive()
    {
        var groups = await GetOkAsync($"/api/reports/groups?runs={Both}&lens=basket");

        groups.GetProperty("nonAdditive").GetBoolean().ShouldBeTrue();
        var byKey = groups.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("parts")[0].GetProperty("value").GetString()!, g => g);
        byKey.Keys.ShouldBe([ReportFixture.Basket1.ToString("D"), ReportFixture.Basket2.ToString("D"), ReportFixture.Basket3.ToString("D")], ignoreOrder: true);
        byKey[ReportFixture.Basket1.ToString("D")].GetProperty("n").GetInt32().ShouldBe(62);
        Number(byKey[ReportFixture.Basket1.ToString("D")].GetProperty("totalR")).ShouldBe(2, 1e-12);
        byKey[ReportFixture.Basket2.ToString("D")].GetProperty("n").GetInt32().ShouldBe(30);
        Number(byKey[ReportFixture.Basket2.ToString("D")].GetProperty("totalR")).ShouldBe(-6, 1e-12);
        byKey[ReportFixture.Basket3.ToString("D")].GetProperty("n").GetInt32().ShouldBe(31);
        Number(byKey[ReportFixture.Basket3.ToString("D")].GetProperty("totalR")).ShouldBe(9, 1e-12);
        groups.GetProperty("evidence").GetProperty("n").GetInt32().ShouldBe(93);
    }

    [Fact]
    public async Task The_style_comes_from_the_stored_strategy_and_the_exit_from_the_stored_comparison()
    {
        var runs = await GetOkAsync("/api/reports/runs");
        var byStyle = await GetOkAsync($"/api/reports/groups?runs={Both}&lens=style,exit");

        var a = runs.GetProperty("runs").EnumerateArray().Single(r => r.GetProperty("runKey").GetString() == ReportFixture.RunA.RunKey);
        a.GetProperty("style").GetString().ShouldBe("intraday");
        a.GetProperty("styleSource").GetString().ShouldBe("stored strategy");
        a.GetProperty("exit").GetString().ShouldBe("FIXED_RR{rr=2}");
        a.GetProperty("exitSource").GetString()!.ShouldStartWith("stored comparison ");
        a.GetProperty("closedTrades").GetInt32().ShouldBe(62);
        byStyle.GetProperty("groups").EnumerateArray().Select(g => (g.GetProperty("key").GetString(), g.GetProperty("n").GetInt32())).ShouldBe(
        [
            ("Style=intraday|Exit=FIXED_RR{rr=2}", 62),
            ("Style=swing|Exit=FIXED_RR{rr=3}", 31),
        ]);
    }

    [Fact]
    public async Task The_runs_list_marks_the_default_set_and_says_why_a_run_is_left_out_and_net_is_unavailable_with_the_reason()
    {
        var runs = await GetOkAsync("/api/reports/runs");

        var items = runs.GetProperty("runs").EnumerateArray().ToList();
        items.Single(r => r.GetProperty("runKey").GetString() == ReportFixture.RunA.RunKey).GetProperty("inDefaultSet").GetBoolean().ShouldBeTrue();
        items.Single(r => r.GetProperty("runKey").GetString() == ReportFixture.RunB.RunKey).GetProperty("inDefaultSet").GetBoolean().ShouldBeTrue();
        var duplicate = items.Single(r => r.GetProperty("runKey").GetString() == ReportFixture.RunADuplicate.RunKey);
        duplicate.GetProperty("inDefaultSet").GetBoolean().ShouldBeFalse();
        duplicate.GetProperty("defaultSetNote").GetString().ShouldBe("an older run of the same strategy and universe");
        runs.GetProperty("net").GetProperty("state").GetString().ShouldBe("unavailable");
        runs.GetProperty("net").GetProperty("reason").GetString()!.ShouldContain("not confirmed");
        runs.GetProperty("defaultSetRule").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_pivot_of_basket_by_strategy_has_the_totals_where_trades_fell_and_empty_cells_elsewhere()
    {
        var pivot = await GetOkAsync($"/api/reports/pivot?runs={Both}&lens=basket,strategy&metric=total_r");

        pivot.GetProperty("metric").GetString().ShouldBe("total_r");
        pivot.GetProperty("rows").GetArrayLength().ShouldBe(3);
        pivot.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ShouldBe(["RPTA", "RPTB"]);
        var cells = pivot.GetProperty("cells").EnumerateArray().ToList();
        cells.Count.ShouldBe(6);
        Number(Cell(cells, ReportFixture.Basket1, "RPTA").GetProperty("value")).ShouldBe(2, 1e-12);
        Number(Cell(cells, ReportFixture.Basket2, "RPTA").GetProperty("value")).ShouldBe(-6, 1e-12);
        Number(Cell(cells, ReportFixture.Basket3, "RPTB").GetProperty("value")).ShouldBe(9, 1e-12);
        var gap = Cell(cells, ReportFixture.Basket1, "RPTB");
        gap.GetProperty("n").GetInt32().ShouldBe(0);
        gap.GetProperty("value").GetProperty("state").GetString().ShouldBe("empty");
        gap.GetProperty("value").TryGetProperty("number", out var number).ShouldBeTrue();
        number.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static JsonElement Cell(List<JsonElement> cells, Guid basket, string strategy) =>
        cells.Single(c => c.GetProperty("row").GetString() == basket.ToString("D") && c.GetProperty("column").GetString() == strategy);

    [Fact]
    public async Task The_equity_curve_and_the_distribution_end_at_the_total_and_count_every_trade()
    {
        var equity = await GetOkAsync($"/api/reports/equity?runs={Both}&lens=strategy");
        var distribution = await GetOkAsync($"/api/reports/distribution?runs={Both}&lens=strategy");

        var series = equity.GetProperty("series").EnumerateArray().ToList();
        series.Select(s => s.GetProperty("key").GetString()).ShouldBe(["Strategy=RPTA", "Strategy=RPTB"]);
        series[0].GetProperty("points").EnumerateArray().Last().GetProperty("cumulativeR").GetDouble().ShouldBe(2, 1e-12);
        series[1].GetProperty("points").EnumerateArray().Last().GetProperty("cumulativeR").GetDouble().ShouldBe(9, 1e-12);
        series[0].GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("trades").GetInt32()).ShouldBe(62);
        var groups = distribution.GetProperty("groups").EnumerateArray().ToList();
        distribution.GetProperty("binWidthR").GetDouble().ShouldBe(0.5);
        var rptb = groups[1].GetProperty("bins").EnumerateArray().ToList();
        rptb.Sum(b => b.GetProperty("count").GetInt32()).ShouldBe(31);
        rptb.Single(b => b.GetProperty("lowerR").ValueKind == JsonValueKind.Number && b.GetProperty("lowerR").GetDouble() == 1.5).GetProperty("count").GetInt32().ShouldBe(16);
        rptb.Single(b => b.GetProperty("lowerR").ValueKind == JsonValueKind.Number && b.GetProperty("lowerR").GetDouble() == -1).GetProperty("count").GetInt32().ShouldBe(15);
    }

    [Fact]
    public async Task The_insights_are_the_eight_in_order_and_what_works_ranks_the_one_cell_with_enough_evidence()
    {
        var insights = await GetOkAsync($"/api/reports/insights?runs={Both}");
        var works = await GetOkAsync($"/api/reports/what-works?runs={Both}");

        insights.GetProperty("insights").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).ShouldBe(["best_strategy", "weakest_strategy", "basket_gap", "side_bias", "best_exit", "weekday", "regime", "calibration"]);
        var best = insights.GetProperty("insights")[0];
        best.GetProperty("state").GetString().ShouldBe("value");
        best.GetProperty("text").GetString()!.ShouldContain("RPTB");
        insights.GetProperty("insights")[6].GetProperty("state").GetString().ShouldBe("unavailable");
        insights.GetProperty("insights")[7].GetProperty("state").GetString().ShouldBe("not_evaluable");
        works.GetProperty("kind").GetString().ShouldBe("ranked");
        works.GetProperty("cellsLookedAt").GetInt32().ShouldBe(3);
        works.GetProperty("requiredTrades").GetInt32().ShouldBe(42);
        works.GetProperty("ranked").GetArrayLength().ShouldBe(1);
        works.GetProperty("ranked")[0].GetProperty("group").GetProperty("n").GetInt32().ShouldBe(62);
        works.GetProperty("notRanked").GetArrayLength().ShouldBe(2);
        works.GetProperty("selectionNote").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_trade_list_pages_through_every_closed_trade_once_and_the_last_page_has_no_cursor()
    {
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await GetOkAsync($"/api/reports/trades?runs={ReportFixture.RunB.RunKey}&limit=10" + (cursor is null ? string.Empty : "&cursor=" + cursor));
            page.GetProperty("total").GetInt32().ShouldBe(31);
            page.GetProperty("evidence").GetProperty("n").GetInt32().ShouldBe(31);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("tradeKey").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        pages.ShouldBe(4);
        seen.Count.ShouldBe(31);
        seen.Distinct().Count().ShouldBe(31);
    }

    [Fact]
    public async Task Open_at_end_trades_are_left_out_and_the_report_says_how_many()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo(), strategy: "RPTOPEN", fixtureEdges: false);
        using (var scope = Factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
        }

        var trades = await GetOkAsync($"/api/reports/trades?runs={run.RunKey}&limit=1000");

        run.OpenAtEnd.Count.ShouldBeGreaterThan(0);
        trades.GetProperty("total").GetInt32().ShouldBe(run.Trades.Count);
        trades.GetProperty("evidence").GetProperty("openAtEndLeftOut").GetInt32().ShouldBe(run.OpenAtEnd.Count);
        trades.GetProperty("evidence").GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain(l => l!.Contains("still open at the end", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Filters_narrow_the_trades_pushed_down_to_the_store()
    {
        var bySymbol = await GetOkAsync($"/api/reports/summary?runs={Both}&symbol=NSE:RPT3");
        var byBasket = await GetOkAsync($"/api/reports/summary?runs={Both}&basket={ReportFixture.Basket2:D}");
        var byDates = await GetOkAsync($"/api/reports/summary?runs={Both}&from=2026-03-02&to=2026-03-06");
        var byStyle = await GetOkAsync($"/api/reports/summary?runs={Both}&style=swing");

        bySymbol.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(31);
        byBasket.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(30);
        byDates.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(15);
        byStyle.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(31);
        byStyle.GetProperty("evidence").GetProperty("runKeys").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_second_run_of_the_same_strategy_and_universe_over_an_overlapping_window_is_refused_with_409()
    {
        var problem = await GetProblemAsync($"/api/reports/summary?runs={ReportFixture.RunA.RunKey},{ReportFixture.RunADuplicate.RunKey}");

        problem.ShouldBe((HttpStatusCode.Conflict, "reports.run_overlap"));
    }

    [Theory]
    [InlineData("/api/reports/groups?lens=planet", "reports.invalid")]
    [InlineData("/api/reports/groups?basis=both", "reports.invalid")]
    [InlineData("/api/reports/pivot?metric=sharpe", "reports.invalid")]
    [InlineData("/api/reports/equity?from=yesterday", "reports.invalid")]
    [InlineData("/api/reports/trades?limit=5000", "reports.invalid")]
    [InlineData("/api/reports/trades?limit=abc", "reports.invalid")]
    [InlineData("/api/reports/trades?cursor=o99999999999", "reports.invalid")]
    [InlineData("/api/reports/pivot?lens=strategy", "reports.invalid")]
    [InlineData("/api/reports/groups?lens=regime", "reports.dimension_unavailable")]
    [InlineData("/api/reports/groups?lens=account", "reports.dimension_unavailable")]
    public async Task An_unknown_name_or_an_unavailable_dimension_is_422_with_its_code(string path, string code)
    {
        var problem = await GetProblemAsync(path);

        problem.ShouldBe((HttpStatusCode.UnprocessableEntity, code));
    }

    [Fact]
    public async Task An_unknown_run_is_404()
    {
        var problem = await GetProblemAsync("/api/reports/summary?runs=" + new string('0', 64));

        problem.ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
    }

    [Fact]
    public async Task A_net_report_is_refused_without_the_owners_stored_confirmation_whatever_the_request_says()
    {
        var plain = await GetProblemAsync($"/api/reports/summary?runs={Both}&basis=net");
        using var smuggled = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/reports/summary?runs={Both}&basis=net&confirmed=true&chargeSheetConfirmed=true&sheet_id=fixture-a&confirmedOn=2026-10-01&netConfirmed=true", UriKind.Relative));
        smuggled.Headers.Add("X-Charge-Sheet-Confirmed", "true");
        smuggled.Headers.Add("X-Net-Confirmed", "fixture-a");
        using var response = await Client.SendAsync(smuggled, Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        plain.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.GetProperty("code").GetString().ShouldBe("reports.net_not_confirmed");
    }

    [Fact]
    public async Task With_the_stored_confirmation_a_net_report_over_runs_costed_with_that_sheet_is_made_and_prints_what_was_confirmed()
    {
        var run = StoredRunFixtures.Run(costed: true, to: RunStoreRoundTripTests.NextTo(), strategy: "RPTNET", fixtureEdges: false);
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "test", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
        }

        var before = await GetProblemAsync($"/api/reports/summary?runs={run.RunKey}&basis=net");
        var on = await ConfirmAsync("fixture-a");
        var net = await GetOkAsync($"/api/reports/summary?runs={run.RunKey}&basis=net");
        var gross = await GetOkAsync($"/api/reports/summary?runs={run.RunKey}");
        var runs = await GetOkAsync("/api/reports/runs");

        before.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
        net.GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("net");
        net.GetProperty("evidence").GetProperty("netBasisConfirmation").GetString().ShouldBe($"charge sheet 'fixture-a' with rates {FixtureSheet.Sheet.RatesSha256[..12]} confirmed by the owner on {on}");
        Number(net.GetProperty("all").GetProperty("totalR")).ShouldBe(run.Trades.Sum(t => t.Trade.Costing!.RNet), 1e-9);
        Number(gross.GetProperty("all").GetProperty("totalR")).ShouldBe(run.Trades.Sum(t => t.Trade.RFilled), 1e-9);
        net.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(run.Trades.Count);
        runs.GetProperty("net").GetProperty("state").GetString().ShouldBe("value");
        runs.GetProperty("runs").EnumerateArray().Single(r => r.GetProperty("runKey").GetString() == run.RunKey).GetProperty("netPossible").GetBoolean().ShouldBeTrue();
        runs.GetProperty("runs").EnumerateArray().Single(r => r.GetProperty("runKey").GetString() == ReportFixture.RunA.RunKey).GetProperty("netPossible").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_net_report_over_a_gross_run_is_refused_even_with_the_confirmation()
    {
        await StoreCostedRunAsync("RPTNETG");
        await ConfirmAsync("fixture-a");

        var problem = await GetProblemAsync($"/api/reports/summary?runs={ReportFixture.RunA.RunKey}&basis=net");

        problem.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        problem.Code.ShouldBe("reports.net_unavailable");
    }

    [Fact]
    public async Task A_confirmation_that_names_no_stored_sheet_counts_as_not_confirmed()
    {
        await ConfirmAsync("no-such-sheet");

        var runs = await GetOkAsync("/api/reports/runs");
        var problem = await GetProblemAsync($"/api/reports/summary?runs={Both}&basis=net");

        runs.GetProperty("net").GetProperty("state").GetString().ShouldBe("unavailable");
        problem.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
    }

    [Fact]
    public async Task A_hand_written_settings_row_is_not_a_confirmation_only_the_owners_log_is_read()
    {
        await StoreCostedRunAsync("RPTNETH");
        await ExecuteSqlAsync($"INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('reports.charge_sheet_confirmation', '{{\"sheet_id\":\"fixture-a\",\"rates_sha256\":\"{FixtureSheet.Sheet.RatesSha256}\",\"confirmed_on\":\"2026-10-01\"}}', 'test', now())");

        var problem = await GetProblemAsync($"/api/reports/summary?runs={Both}&basis=net");
        var runs = await GetOkAsync("/api/reports/runs");

        problem.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
        runs.GetProperty("net").GetProperty("state").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_confirmation_made_through_the_owners_endpoint_is_what_makes_a_report_net_and_a_revoke_makes_it_gross_again()
    {
        var run = await StoreCostedRunAsync("RPTNETE");
        var net = $"/api/reports/summary?runs={run.RunKey}&basis=net";
        (await GetProblemAsync(net)).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));

        using var on = await Client.PutAsJsonAsync(new Uri("/api/charge-sheet/capability", UriKind.Relative), new { on = true, reason = "I will confirm my sheet", password = Factory.OwnerPassword }, Ct);
        using var confirm = await Client.PostAsJsonAsync(new Uri("/api/charge-sheet/confirmation", UriKind.Relative), new { sheetId = "fixture-a", ratesSha256 = FixtureSheet.Sheet.RatesSha256, reason = "checked against my contract notes", password = Factory.OwnerPassword }, Ct);
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetOkAsync(net)).GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("net");
        using var revoke = await Client.PostAsJsonAsync(new Uri("/api/charge-sheet/confirmation/revoke", UriKind.Relative), new { reason = "changed my mind", password = Factory.OwnerPassword }, Ct);
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetProblemAsync(net)).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
    }

    [Fact]
    public async Task A_revoked_confirmation_makes_every_report_gross_again()
    {
        var run = await StoreCostedRunAsync("RPTNETR");
        await ConfirmAsync("fixture-a");
        (await GetOkAsync($"/api/reports/summary?runs={run.RunKey}&basis=net")).GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("net");

        await ExecuteSqlAsync($"INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('revoke', 'fixture-a', '{FixtureSheet.Sheet.RatesSha256}', 'owner:test@example.com', 'test')");
        var problem = await GetProblemAsync($"/api/reports/summary?runs={run.RunKey}&basis=net");

        problem.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
    }

    [Fact]
    public async Task A_stored_confirmation_stays_what_it_is_whatever_sheet_the_request_names()
    {
        var run = await StoreCostedRunAsync("RPTNET2");
        var on = await ConfirmAsync("fixture-a");

        var net = await GetOkAsync($"/api/reports/summary?runs={run.RunKey}&basis=net&sheet_id=other-sheet&sheet=other&rates_sha256={new string('0', 64)}&confirmed_on=2020-01-01");

        net.GetProperty("evidence").GetProperty("netBasisConfirmation").GetString().ShouldBe($"charge sheet 'fixture-a' with rates {FixtureSheet.Sheet.RatesSha256[..12]} confirmed by the owner on {on}");
    }

    [Fact]
    public async Task A_confirmation_of_rates_the_stored_sheet_no_longer_has_is_not_a_confirmation()
    {
        var run = await StoreCostedRunAsync("RPTNET3");
        await ConfirmAsync("fixture-a", new string('1', 64));

        var problem = await GetProblemAsync($"/api/reports/summary?runs={run.RunKey}&basis=net");

        problem.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
    }

    private async Task<XDrishti.Domain.Research.SimulationRun> StoreCostedRunAsync(string strategy)
    {
        var run = StoredRunFixtures.Run(costed: true, to: RunStoreRoundTripTests.NextTo(), strategy: strategy, fixtureEdges: false);
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "test", DateTimeOffset.UnixEpoch, Ct);
        (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        (await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
        return run;
    }

    [Fact]
    public async Task Gross_and_costed_runs_in_one_set_are_409_over_http()
    {
        var costed = await StoreCostedRunAsync("RPTMIX");

        var problem = await GetProblemAsync($"/api/reports/summary?runs={ReportFixture.RunA.RunKey},{costed.RunKey}");

        problem.ShouldBe((HttpStatusCode.Conflict, "reports.mixed_basis"));
    }

    [Fact]
    public async Task The_same_signals_under_two_exits_are_422_double_count_unless_the_lens_has_the_exit()
    {
        var runs = $"{ReportFixture.RunA.RunKey},{ReportFixture.RunAOtherExit.RunKey}";

        var plain = await GetProblemAsync($"/api/reports/summary?runs={runs}");
        var split = await GetOkAsync($"/api/reports/groups?runs={runs}&lens=exit");

        plain.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.double_count"));
        split.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("n").GetInt32()).ShouldBe([62, 62]);
    }

    [Fact]
    public async Task A_trade_cursor_from_another_report_is_422()
    {
        using var page = await Client.GetAsync(new Uri($"/api/reports/trades?runs={ReportFixture.RunB.RunKey}&limit=10", UriKind.Relative), Ct);
        var cursor = (await page.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("nextCursor").GetString()!;

        var problem = await GetProblemAsync($"/api/reports/trades?runs={ReportFixture.RunA.RunKey}&limit=10&cursor={cursor}");

        problem.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
    }

    [Fact]
    public async Task A_stored_trade_replays_with_its_facts_and_says_no_bars_when_the_instrument_has_none()
    {
        var replay = await GetOkAsync($"/api/reports/trades/{ReportFixture.RunA.RunKey}/0?timeframe=15m");

        replay.GetProperty("ordinal").GetInt32().ShouldBe(0);
        replay.GetProperty("basis").GetString().ShouldBe("gross");
        replay.GetProperty("trade").GetProperty("symbol").GetString().ShouldBe("NSE:RPT1");
        replay.GetProperty("trade").GetProperty("stopPrice").GetDouble().ShouldBe(98);
        replay.GetProperty("legs").GetArrayLength().ShouldBe(0);
        replay.GetProperty("chart").GetProperty("timeframe").GetString().ShouldBe("15m");
        replay.GetProperty("chart").GetProperty("coherence").GetProperty("state").GetString().ShouldBe("no_bars");
        replay.GetProperty("features").GetProperty("state").GetString().ShouldBe("unavailable");
        replay.GetProperty("evidence").GetProperty("n").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_trade_list_carries_the_ordinal_that_addresses_the_replay()
    {
        var page = await GetOkAsync($"/api/reports/trades?runs={ReportFixture.RunB.RunKey}&limit=1");

        var ordinal = page.GetProperty("items")[0].GetProperty("ordinal").GetInt32();
        var replay = await GetOkAsync($"/api/reports/trades/{ReportFixture.RunB.RunKey}/{ordinal}");

        replay.GetProperty("tradeKey").GetString().ShouldBe(page.GetProperty("items")[0].GetProperty("tradeKey").GetString());
    }

    [Theory]
    [InlineData("0?timeframe=10m", HttpStatusCode.UnprocessableEntity, "reports.invalid")]
    [InlineData("0?before=9", HttpStatusCode.UnprocessableEntity, "reports.invalid")]
    [InlineData("9999", HttpStatusCode.NotFound, "reports.not_found")]
    [InlineData("0?openAtEnd=true", HttpStatusCode.NotFound, "reports.not_found")]
    public async Task A_replay_that_cannot_be_made_is_refused_with_its_status_and_code(string tail, HttpStatusCode status, string code) =>
        (await GetProblemAsync($"/api/reports/trades/{ReportFixture.RunA.RunKey}/{tail}")).ShouldBe((status, code));

    [Fact]
    public async Task An_unknown_run_replay_is_404()
    {
        var problem = await GetProblemAsync($"/api/reports/trades/{new string('b', 64)}/0");

        problem.ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
    }

    [Fact]
    public async Task The_endpoints_are_read_only_and_need_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();
        using var post = await Client.PostAsync(new Uri("/api/reports/summary", UriKind.Relative), null, Ct);

        foreach (var path in new[] { "runs", "summary", "groups", "pivot", "equity", "distribution", "insights", "what-works", "trades", "trades/" + ReportFixture.RunA.RunKey + "/0" })
        {
            using var response = await anonymous.GetAsync(new Uri("/api/reports/" + path, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }

        post.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_default_run_set_is_used_when_no_run_is_named_and_the_evidence_says_so()
    {
        var summary = await GetOkAsync("/api/reports/groups?lens=strategy&strategy=RPTA,RPTB");

        summary.GetProperty("evidence").GetProperty("defaultRunSet").GetBoolean().ShouldBeTrue();
        summary.GetProperty("evidence").GetProperty("runKeys").EnumerateArray().Select(k => k.GetString()).ShouldContain(ReportFixture.RunA.RunKey);
        summary.GetProperty("evidence").GetProperty("runKeys").EnumerateArray().Select(k => k.GetString()).ShouldNotContain(ReportFixture.RunADuplicate.RunKey);
        summary.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("n").GetInt32()).ShouldBe([62, 31]);
    }
}
