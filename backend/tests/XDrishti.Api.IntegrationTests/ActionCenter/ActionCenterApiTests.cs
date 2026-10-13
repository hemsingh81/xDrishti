using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Portfolio.Actions;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>
/// The Action center over real holdings, lots, master sectors and baskets on TimescaleDB (P2-09; see <see cref="ActionCenterTestBase"/> for the data): the items and counts, the reads it makes, scope and filters, and the answers of the decisions.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed partial class ActionCenterApiTests(ApiFactory factory) : ActionCenterTestBase(factory)
{
    [Fact]
    public async Task The_items_are_listed_most_important_first_with_their_numbers_and_the_counts_of_the_scope()
    {
        var list = await ActionsAsync();

        list.Items.Select(i => (i.Rule, i.Symbol, i.Severity)).ShouldBe(
        [
            ("below_cost", "AAA", "high"),
            ("overweight", "AAA", "medium"),
            ("sector_concentration", null, "medium"),
            ("large_gain", "CCC", "info"),
            ("not_in_basket", "AAA", "info"),
            ("not_in_basket", "CCC", "info"),
        ]);
        list.Items.ShouldAllBe(i => i.State == "open" && i.AccountId == Account.Id && i.AccountName == "Example One");
        var below = list.Items[0];
        (below.Numbers.PnlPct, below.Numbers.Pnl, below.Numbers.Invested, below.Numbers.Value, below.Numbers.ThresholdPct).ShouldBe((-30m, -3000m, 10000m, 7000m, 20m));
        below.Detail.ShouldBe("Down 30.0 % from cost (₹10,000 invested)");
        below.InstrumentId.ShouldBe(Aaa);
        below.Suggestions.ShouldBe(["open", "set_alert", "add_to_basket", "snooze", "done"]);
        var sector = list.Items[2];
        (sector.HoldingId, sector.InstrumentId, sector.Numbers.Sector, sector.Numbers.SectorSharePct, sector.Numbers.LargestSymbol).ShouldBe((null, null, "Banks", 85.1064m, "AAA"));
        list.Items[1].Numbers.WeightPct.ShouldBe(74.4681m);
        list.Counts.Open.ShouldBe(6);
        (list.Counts.Snoozed, list.Counts.Done, list.Counts.NoPrice).ShouldBe((0, 0, 0));
        list.Counts.ByRule.ShouldBe(new Dictionary<string, int>
        {
            ["below_cost"] = 1,
            ["large_gain"] = 1,
            ["overweight"] = 1,
            ["sector_concentration"] = 1,
            ["long_term_soon"] = 0,
            ["lagging_nifty"] = 0,
            ["not_in_basket"] = 2,
        });
        list.Counts.BySeverity.ShouldBe(new Dictionary<string, int> { ["high"] = 1, ["medium"] = 2, ["info"] = 3 });
        list.Counts.Unavailable.Lagging.ShouldBe(3); // held for two years, but the master has no benchmark index to compare with
    }

    [Fact]
    public async Task Every_item_id_is_safe_in_a_url_path_and_stable_between_reads()
    {
        var first = await ActionsAsync();
        var second = await ActionsAsync();

        first.Items.Select(i => i.Id).ShouldBe(second.Items.Select(i => i.Id));
        first.Items.ShouldAllBe(i => IdShape().IsMatch(i.Id));
        first.Items.Select(i => i.Id).Distinct().Count().ShouldBe(first.Items.Count);
    }

    [Fact]
    public async Task The_request_reads_holdings_lots_sectors_baskets_decisions_and_settings_once_and_no_bars_without_a_benchmark()
    {
        await ActionsAsync(); // warm up (the first request may prepare statements)

        var reads = await Client.StatementsDuringAsync(async () => await ActionsAsync());

        reads.Count(s => s.Contains("portfolio.holdings ", StringComparison.Ordinal)).ShouldBe(1, string.Join("\n", reads));
        reads.Count(s => s.Contains("portfolio.holding_lots", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("market.instruments", StringComparison.Ordinal) && !s.Contains("portfolio.holdings", StringComparison.Ordinal) && !s.Contains("market.bar_coverage", StringComparison.Ordinal)
            && s.Contains("sector", StringComparison.OrdinalIgnoreCase)).ShouldBe(1, string.Join("\n", reads));
        reads.Count(s => s.Contains("market.basket_members", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("portfolio.action_states", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("platform.app_settings", StringComparison.Ordinal)).ShouldBe(2); // the thresholds, and the benchmark setting (the holdings are old enough for the lagging rule)
        reads.Count(s => s.Contains("market.bars_agg", StringComparison.Ordinal)).ShouldBe(0);
    }

    [Fact]
    public async Task With_a_benchmark_index_the_bars_are_read_once_and_a_holding_far_behind_it_is_lagging()
    {
        // The index doubles in the two years the holdings were held (about 41 % a year): AAA (-30 %), BBB (flat) and CCC (+40 %, about 18 % a year) all lag it by more than 10 points.
        await IndexAsync("NSE:NIFTY", "Nifty 50", (new DateOnly(2024, 10, 11), 10000.0), (new DateOnly(2026, 10, 9), 20000.0));
        await ActionsAsync();

        ActionList list = null!;
        var reads = await Client.StatementsDuringAsync(async () => list = await ActionsAsync());

        reads.Count(s => s.Contains("market.bars_agg", StringComparison.Ordinal)).ShouldBe(1, string.Join("\n", reads));
        list.Items.Where(i => i.Rule == "lagging_nifty").Select(i => i.Symbol).ShouldBe(["AAA", "BBB", "CCC"], ignoreOrder: true);
        list.Counts.Unavailable.Lagging.ShouldBe(0);
        var aaa = list.Items.Single(i => i is { Rule: "lagging_nifty", Symbol: "AAA" });
        (aaa.Numbers.HeldDays, aaa.Numbers.XirrPct < 0m, aaa.Numbers.BenchmarkXirrPct > 30m, aaa.Numbers.GapPp < -10m).ShouldBe((730, true, true, true));
        aaa.Severity.ShouldBe("medium");
    }

    [Fact]
    public async Task The_account_scope_reads_one_account_and_an_unknown_one_is_not_found()
    {
        var own = await ActionsAsync($"?accountId={Account.Id}");
        using var unknown = await Client.GetAsync($"/api/portfolio/actions?accountId={Guid.NewGuid()}", Ct);

        own.Counts.Open.ShouldBe(6);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.not_found");
    }

    [Fact]
    public async Task An_account_that_does_not_count_in_totals_is_left_out_unless_chosen()
    {
        using var saved = await Client.PutJsonAsync($"/api/accounts/{Account.Id}/portfolio", new { includeInTotals = false }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));

        var all = await ActionsAsync();
        var own = await ActionsAsync($"?accountId={Account.Id}");

        all.Items.ShouldBeEmpty();
        own.Counts.Open.ShouldBe(6);
    }

    [Fact]
    public async Task A_bucket_keeps_the_items_of_its_holdings_and_an_unknown_bucket_gives_none()
    {
        using var created = await Client.PostJsonAsync("/api/portfolio-buckets", new { name = "Growth" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var growth = (await created.Content.ReadFromJsonAsync<BucketView>(Ct))!.Id;
        await ExecuteAsync("UPDATE portfolio.holdings SET bucket_id = @b WHERE trading_symbol = 'CCC'", ("b", growth));

        var inGrowth = await ActionsAsync($"?bucketId={growth}");
        var none = await ActionsAsync($"?bucketId={Guid.NewGuid()}");

        inGrowth.Items.Select(i => (i.Rule, i.Symbol)).ShouldBe([("large_gain", "CCC"), ("not_in_basket", "CCC")]);
        inGrowth.Counts.Open.ShouldBe(2);
        none.Items.ShouldBeEmpty();
        none.Counts.Open.ShouldBe(0);
    }

    [Theory]
    [InlineData("?status=pending")]
    [InlineData("?status=closed")]
    public async Task An_unknown_status_is_a_400_with_the_field(string query)
    {
        using var response = await Client.GetAsync("/api/portfolio/actions" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["status"]);
    }

    [Fact]
    public async Task Snooze_done_and_reopen_answer_with_the_new_state_and_the_item()
    {
        var below = (await ActionsAsync()).Items[0];

        var snoozed = await DecidedAsync(below.Id, "snooze");
        var done = await DecidedAsync(below.Id, "done");
        var reopened = await DecidedAsync(below.Id, "reopen");

        (snoozed.State, snoozed.SnoozedUntil, snoozed.Item!.Id).ShouldBe(("snoozed", new DateOnly(2026, 10, 19), below.Id));
        (done.State, done.SnoozedUntil, done.Item!.State).ShouldBe(("done", null, "done"));
        (reopened.State, reopened.Item!.State).ShouldBe(("open", "open"));
    }

    [Fact]
    public async Task A_stale_id_is_404_not_found_for_snooze_and_done_but_not_for_reopen()
    {
        var stale = $"below_cost:{Guid.NewGuid()}";

        using var snooze = await DecideAsync(stale, "snooze");
        using var done = await DecideAsync(stale, "done");
        using var reopen = await DecideAsync(stale, "reopen");
        using var junk = await DecideAsync("junk", "reopen");

        snooze.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await snooze.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.not_found");
        done.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        reopen.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reopen.ReadAsync<ActionDecisionView>(Ct)).Item.ShouldBeNull();
        junk.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountAsync("SELECT count(*) FROM portfolio.action_states")).ShouldBe(0);
    }

    [Fact]
    public async Task A_sector_id_with_odd_characters_in_the_sector_name_works_in_the_path()
    {
        await ExecuteAsync("UPDATE market.instruments SET sector = 'Oil & Gas / Energy: ₹ ünï?x=1#a' WHERE id = @a OR id = @b", ("a", Aaa), ("b", Bbb));
        var item = (await ActionsAsync()).Items.Single(i => i.Rule == "sector_concentration");

        var done = await DecidedAsync(item.Id, "done");

        item.Numbers.Sector.ShouldBe("Oil & Gas / Energy: ₹ ünï?x=1#a");
        IdShape().IsMatch(item.Id).ShouldBeTrue(item.Id);
        done.Item!.State.ShouldBe("done");
        (await ActionsAsync()).Items.Any(i => i.Rule == "sector_concentration").ShouldBeFalse();
    }

    [Fact]
    public async Task A_snooze_counts_its_seven_days_on_the_india_date()
    {
        // 20:00 UTC on 12 October is 01:30 on 13 October in India: the seven days end on 20 October.
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero));
        var later = await Factory.SignInAsync(Ct); // the first session has gone idle meanwhile
        using var list = await later.Client.GetAsync("/api/portfolio/actions", Ct);
        var below = (await list.ReadAsync<ActionList>(Ct)).Items[0];

        using var response = await later.Client.PostAsync($"/api/portfolio/actions/{below.Id}/snooze", Ct);
        var snoozed = await response.ReadAsync<ActionDecisionView>(Ct);

        snoozed.SnoozedUntil.ShouldBe(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public async Task A_lot_that_cost_nothing_gives_no_error_and_works_in_the_items_the_notices_and_the_decisions()
    {
        // bonus shares: the database allows a unit cost of 0. CCC's only lot turns 12 months old in 20 days.
        await ExecuteAsync("UPDATE portfolio.holding_lots SET unit_cost = 0, acquired_on = date '2025-11-01' WHERE holding_id = (SELECT id FROM portfolio.holdings WHERE trading_symbol = 'CCC')");
        InvalidateHistory();

        var list = await ActionsAsync();
        var notices = await NoticesAsync();

        var soon = list.Items.Single(i => i is { Rule: "long_term_soon", Symbol: "CCC" });
        var lot = soon.Numbers.Lots.ShouldNotBeNull().ShouldHaveSingleItem();
        (lot.AcquiredOn, lot.LongTermOn, lot.DaysLeft, lot.GainPct).ShouldBe((new DateOnly(2025, 11, 1), new DateOnly(2026, 11, 1), 20, null));
        list.Items.Any(i => i is { Rule: "large_gain", Symbol: "CCC" }).ShouldBeFalse(); // no cost, no percentage
        notices.Items.Select(n => n.Kind).ShouldContain("actions_need_decision");
        (await DecidedAsync(soon.Id, "snooze")).State.ShouldBe("snoozed");
        (await DecidedAsync(soon.Id, "done")).State.ShouldBe("done");
        (await DecidedAsync(soon.Id, "reopen")).State.ShouldBe("open");
        (await ActionsAsync("?status=all")).Items.Any(i => i.Id == soon.Id).ShouldBeTrue();
    }

    [GeneratedRegex("^[a-z_]+:[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}(:[0-9a-f]{12})?$")]
    private static partial Regex IdShape();
}
