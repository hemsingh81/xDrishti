using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Selection;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// P15-10 on a real TimescaleDB: GET /api/research/extensions and /{id} (criterion 8), the verb's use case on today's empty database (the honest NOT_EVALUABLE of every family, ledger untouched, a re-run stores nothing), and the event-filter auto path end to end through the real reader over
/// stored rows (criterion 6): a KEEP for the switch's N with <c>approval = auto</c> turns the filter ON recorded as auto, a KEEP for another N does nothing, manual does nothing, and a later DROP turns it OFF, recorded as auto.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExtensionEvaluationApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri List = new("/api/research/extensions", UriKind.Relative);

    private static ExtensionEvaluationRecord Filter(int n, string verdict, string id, string? confirms = null) =>
        new(id.PadRight(64, '0'), "filter:results", "F", null, $"evf-1:N={n}", "none", new string('a', 64), null, new string('b', 64), 3, 1.4, "quarter", 8, 8, 1000, null, "{\"removed_share\":0.2}", verdict, [], ["limit"], "{\"verdict\":\"" + verdict + "\"}",
            "{\"sources\":{\"development_record\":\"" + (confirms is null ? "none" : confirms.PadRight(64, '0')) + "\"}}", confirms is not null);

    private async Task StoreAsync(ExtensionEvaluationRecord record)
    {
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IExtensionEvaluationStore>().AppendAsync(record, DateTimeOffset.UtcNow, Ct)).IsSuccess.ShouldBeTrue();
        await Task.Delay(5, Ct);   // created_at orders the newest verdict; two writes are never in one tick
    }

    private async Task<EventFilterAutoResult> AutoAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyEventFilterAutoCommand, EventFilterAutoResult>>().HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value;
    }

    private async Task OwnerRowAsync(bool enabled, string approval, int days = 2)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO filings.event_filter_decisions (enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot) VALUES (@e, @d, @a, 'owner', 'owner:x@example.invalid', 'r', '{}'::jsonb)",
            ("e", enabled), ("d", (short)days), ("a", approval));
    }

    private Task<long> AutoRowsAsync() => ScalarAsync<long>("SELECT count(*) FROM filings.event_filter_decisions WHERE decided_by = 'auto'");

    [Fact]
    public async Task Without_a_session_both_routes_are_401()
    {
        using var list = await Factory.CreateAnonymousClient().GetAsync(new Uri(List + "?family=pead", UriKind.Relative), Ct);
        using var one = await Factory.CreateAnonymousClient().GetAsync(new Uri(List + "/" + new string('0', 64), UriKind.Relative), Ct);

        (list.StatusCode, one.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?family=ext:md-1")]
    [InlineData("?family=PEAD")]
    public async Task An_unknown_or_missing_family_is_400_invalid_query(string query)
    {
        using var response = await Client.GetAsync(new Uri(List + query, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("extensions.invalid_query");
    }

    [Fact]
    public async Task A_known_family_with_nothing_stored_is_an_empty_list()
    {
        var body = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "?family=pead", UriKind.Relative), Ct);

        body.GetProperty("family").GetString().ShouldBe("pead");
        body.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_shared_route_lists_the_expression_family_and_its_record_by_id_and_the_p15_families_stay_apart()
    {
        var record = new ExtensionEvaluationRecord(
            "e1".PadRight(64, '0'), "expr:option_buy", "O", null, "expr-1", "none", new string('a', 64), null, new string('b', 64), 3, 1.4, "quarter", 0, 0, 0, null, "{\"mean_d\":\"unavailable\"}", "NOT_EVALUABLE", ["folds 0 of 6"], ["future baseline: NOT_EVALUABLE no_futures_bars"],
            "{\"verdict\":\"NOT_EVALUABLE\"}", "{}", false);
        await StoreAsync(record);

        var body = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "?family=expr:option_buy", UriKind.Relative), Ct);
        var one = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "/" + record.EvaluationId, UriKind.Relative), Ct);
        var pead = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "?family=pead", UriKind.Relative), Ct);

        body.GetProperty("items").GetArrayLength().ShouldBe(1);
        body.GetProperty("items")[0].GetProperty("protocol").GetString().ShouldBe("O");
        one.GetProperty("missing")[0].GetString().ShouldBe("folds 0 of 6");
        one.GetProperty("estimates").GetProperty("mean_d").GetString().ShouldBe("unavailable");
        pead.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_shared_route_lists_the_iv_family_and_its_record_and_the_p15_families_stay_apart()
    {
        var record = new ExtensionEvaluationRecord(
            "e2".PadRight(64, '0'), "ext:iv-1", "X", "iv-1", "iv-1:iv_atm,iv_rank_252", "tf-1", new string('a', 64), null, new string('b', 64), 1, 1.0, "quarter", 0, 0, 0, null, "{\"delta_r\":\"unavailable\"}", "NOT_EVALUABLE", ["no_extraction: extraction records 0 of 1 runs (xd learn features --extension iv-1)"], [],
            "{\"verdict\":\"NOT_EVALUABLE\"}", "{\"sources\":{\"archive_watermark\":\"none\"}}", false);
        await StoreAsync(record);

        var body = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "?family=ext:iv-1", UriKind.Relative), Ct);
        var one = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "/" + record.EvaluationId, UriKind.Relative), Ct);
        var ev = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "?family=ext:ev-1", UriKind.Relative), Ct);

        body.GetProperty("items").GetArrayLength().ShouldBe(1);
        body.GetProperty("items")[0].GetProperty("extensionVersion").GetString().ShouldBe("iv-1");
        one.GetProperty("family").GetString().ShouldBe("ext:iv-1");
        one.GetProperty("missing")[0].GetString().ShouldStartWith("no_extraction");
        ev.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Today_the_iv_family_is_not_evaluable_with_the_missing_extraction_named_the_ledger_is_untouched_and_a_rerun_stores_nothing()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateExtensionCommand, ExtensionEvaluationOutcome>>();

        var first = (await handler.HandleAsync(new EvaluateExtensionCommand("ext:iv-1", false), Ct)).Value;
        var second = (await handler.HandleAsync(new EvaluateExtensionCommand("ext:iv-1", false), Ct)).Value;

        var runsStored = await ScalarAsync<long>("SELECT count(*) FROM research.sim_runs") > 0;
        first.Records.ShouldAllBe(r => r.Verdict == "NOT_EVALUABLE" && r.Missing.Count > 0);
        if (!runsStored)
        {
            first.Records.Single().Missing.Single().ShouldStartWith("no_extraction");
        }

        second.Records.ShouldAllBe(r => !r.Stored);
        second.Records.Select(r => r.EvaluationId).ShouldBe(first.Records.Select(r => r.EvaluationId));
        (await ScalarAsync<long>("SELECT count(*) FROM lab.test_ledger WHERE strategy_id = 'ext:iv-1'")).ShouldBe(0);
    }

    [Fact]
    public async Task Stored_records_come_back_newest_first_and_one_by_id_with_its_evidence_and_an_unknown_id_is_404()
    {
        await StoreAsync(Filter(1, "DROP", "a1"));
        await StoreAsync(Filter(2, "KEEP", "a2"));

        var body = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "?family=filter:results", UriKind.Relative), Ct);
        var items = body.GetProperty("items");
        var one = await Client.GetFromJsonAsync<JsonElement>(new Uri(List + "/" + "a2".PadRight(64, '0'), UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri(List + "/" + new string('f', 64), UriKind.Relative), Ct);

        items.GetArrayLength().ShouldBe(2);
        items[0].GetProperty("versionKey").GetString().ShouldBe("evf-1:N=2");
        items[1].GetProperty("verdict").GetString().ShouldBe("DROP");
        one.GetProperty("verdict").GetString().ShouldBe("KEEP");
        one.GetProperty("evidence").GetProperty("verdict").GetString().ShouldBe("KEEP");
        one.GetProperty("estimates").GetProperty("removed_share").GetDouble().ShouldBe(0.2, 1e-9);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync(Ct)).ShouldContain("extensions.not_found");
    }

    [Fact]
    public async Task Today_every_family_is_not_evaluable_with_the_missing_quantity_named_the_ledger_is_untouched_and_a_rerun_stores_nothing()
    {
        // Other suites may leave stored runs behind; then protocol F judges them (and counts its three trials), and only the other families are held to the empty-history answer.
        var runsStored = await ScalarAsync<long>("SELECT count(*) FROM research.sim_runs") > 0;
        foreach (var family in ExtensionFamilies.All)
        {
            using var scope = Factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateExtensionCommand, ExtensionEvaluationOutcome>>();

            var first = (await handler.HandleAsync(new EvaluateExtensionCommand(family, false), Ct)).Value;
            var second = (await handler.HandleAsync(new EvaluateExtensionCommand(family, false), Ct)).Value;

            if (family != ExtensionFamilies.Filter || !runsStored)
            {
                first.Records.ShouldAllBe(r => r.Verdict == "NOT_EVALUABLE" && r.Missing.Count > 0, family);
            }

            second.Records.ShouldAllBe(r => !r.Stored, family);
            second.Records.Select(r => r.EvaluationId).ShouldBe(first.Records.Select(r => r.EvaluationId), family);
        }

        (await ScalarAsync<long>("SELECT count(*) FROM lab.test_ledger WHERE (strategy_id LIKE '%:%' AND strategy_id <> 'filter:results') OR strategy_id = 'pead'")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM research.extension_evaluations WHERE verdict <> 'NOT_EVALUABLE' AND family <> 'filter:results'")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM research.extension_evaluations")).ShouldBe(3 + 1 + 1 + 2 + 1);
    }

    [Fact]
    public async Task A_stored_evaluation_row_is_visible_through_the_filter_view_as_the_auto_verdict_and_fresh_is_unavailable()
    {
        var fresh = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/selection/event-filter", UriKind.Relative), Ct);
        fresh.GetProperty("autoVerdict").GetString().ShouldBe("unavailable");

        await OwnerRowAsync(false, "manual");
        await StoreAsync(Filter(2, "KEEP", "a2"));
        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/selection/event-filter", UriKind.Relative), Ct);

        view.GetProperty("autoVerdict").GetString().ShouldBe("KEEP_NOT_CONFIRMED");
    }

    [Fact]
    public async Task With_approval_auto_only_a_hold_out_confirmed_keep_for_the_switchs_n_turns_the_filter_on_as_auto_and_a_later_drop_turns_it_off()
    {
        await OwnerRowAsync(false, "auto");
        await StoreAsync(Filter(3, "KEEP", "a3"));
        await StoreAsync(Filter(3, "KEEP", "a3e", confirms: "a3"));

        (await AutoAsync()).ShouldBe(new EventFilterAutoResult("none", "verdict_unavailable"));   // a confirmed KEEP, but for another N
        await StoreAsync(Filter(2, "KEEP", "a2"));
        (await AutoAsync()).ShouldBe(new EventFilterAutoResult("none", "keep_not_confirmed"));     // a development KEEP alone never turns it on
        (await AutoRowsAsync()).ShouldBe(0);

        await StoreAsync(Filter(2, "KEEP", "a2e", confirms: "a2"));
        var on = await AutoAsync();

        on.Action.ShouldBe("turn_on");
        (await AutoRowsAsync()).ShouldBe(1);
        (await ScalarAsync<string>("SELECT verdict_ref FROM filings.event_filter_decisions WHERE decided_by = 'auto' ORDER BY id DESC LIMIT 1")).ShouldBe("a2e".PadRight(64, '0'));
        (await ScalarAsync<string>("SELECT actor FROM filings.event_filter_decisions ORDER BY id DESC LIMIT 1")).ShouldBe("auto:event-filter");
        (await ScalarAsync<bool>("SELECT enabled FROM filings.event_filter_decisions ORDER BY id DESC LIMIT 1")).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT setting_snapshot->>'verdict.hold_out_confirmed' FROM filings.event_filter_decisions ORDER BY id DESC LIMIT 1")).ShouldBe("true");

        await StoreAsync(Filter(2, "DROP", "a4"));
        var off = await AutoAsync();

        off.Action.ShouldBe("turn_off");
        (await ScalarAsync<bool>("SELECT enabled FROM filings.event_filter_decisions ORDER BY id DESC LIMIT 1")).ShouldBeFalse();
        (await ScalarAsync<string>("SELECT decided_by FROM filings.event_filter_decisions ORDER BY id DESC LIMIT 1")).ShouldBe("auto");
        (await ScalarAsync<bool>("SELECT (approval = 'auto' AND results_within_days = 2) FROM filings.event_filter_decisions ORDER BY id DESC LIMIT 1")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_hold_out_then_new_data_and_a_new_development_keep_does_not_turn_the_filter_on()
    {
        await OwnerRowAsync(false, "auto");
        await StoreAsync(Filter(2, "KEEP", "a1"));
        await StoreAsync(Filter(2, "INCONCLUSIVE", "a1e", confirms: "a1"));
        (await AutoAsync()).ShouldBe(new EventFilterAutoResult("none", "already_off"));

        await StoreAsync(Filter(2, "KEEP", "a5"));   // new data, a new population, the reserved seasons already spent

        (await AutoAsync()).ShouldBe(new EventFilterAutoResult("none", "keep_not_confirmed"));
        (await AutoRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task With_approval_manual_a_keep_changes_nothing()
    {
        await OwnerRowAsync(false, "manual");
        await StoreAsync(Filter(2, "KEEP", "a2"));

        (await AutoAsync()).ShouldBe(new EventFilterAutoResult("none", "approval_manual"));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.event_filter_decisions")).ShouldBe(1);
    }
}
