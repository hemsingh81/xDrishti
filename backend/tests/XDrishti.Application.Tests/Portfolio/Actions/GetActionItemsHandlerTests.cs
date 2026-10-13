using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>The Action-center read (acceptance criteria 10 and 1-8 end to end over the ports): scope, filters, counts, order, the reads it makes, and the clock. Monday 2026-10-12 10:00 India time.</summary>
public sealed class GetActionItemsHandlerTests
{
    private static HoldingData Bad() => Pos("BAD", 70m); // -30 %: below cost, high

    private static ActionsHarness With(params HoldingData[] holdings)
    {
        var harness = new ActionsHarness(holdings);
        foreach (var holding in holdings.Where(h => h.InstrumentId is not null))
        {
            harness.Classifications.Baskets[holding.InstrumentId!.Value] = [new BasketRef(Guid.NewGuid(), "Core")]; // in a basket unless a test removes it
        }

        return harness;
    }

    [Fact]
    public async Task The_open_items_come_back_with_counts_by_rule_and_severity()
    {
        var harness = With(Bad(), Pos("WIN", 140m), Pos("OK", 101m));

        var list = await harness.ListAsync();

        list.Items.Select(i => (i.Symbol, i.Rule, i.Severity, i.State)).ShouldBe([("BAD", "below_cost", "high", "open"), ("WIN", "large_gain", "info", "open")]);
        list.Counts.Open.ShouldBe(2);
        (list.Counts.Snoozed, list.Counts.Done, list.Counts.NoPrice).ShouldBe((0, 0, 0));
        list.Counts.ByRule.ShouldBe(new Dictionary<string, int>
        {
            ["below_cost"] = 1,
            ["large_gain"] = 1,
            ["overweight"] = 0,
            ["sector_concentration"] = 0,
            ["long_term_soon"] = 0,
            ["lagging_nifty"] = 0,
            ["not_in_basket"] = 0,
        });
        list.Counts.BySeverity.ShouldBe(new Dictionary<string, int> { ["high"] = 1, ["medium"] = 0, ["info"] = 1 });
        list.Counts.Unavailable.Lagging.ShouldBe(0);
    }

    [Fact]
    public async Task Without_an_account_it_reads_the_accounts_that_count_in_totals_like_the_summary()
    {
        var harness = With(Bad());

        await harness.ListAsync();

        var filter = harness.Queries.LastFilter!;
        (filter.AccountId, filter.BucketId, filter.Status, filter.TotalsOnly).ShouldBe((null, null, "open", true));
    }

    [Fact]
    public async Task With_an_account_it_reads_that_account_whether_or_not_it_counts_in_totals()
    {
        var harness = With(Bad());

        await harness.ListAsync(account: AccountOne);

        var filter = harness.Queries.LastFilter!;
        (filter.AccountId, filter.TotalsOnly).ShouldBe((AccountOne, false));
    }

    [Fact]
    public async Task An_unknown_account_is_not_found()
    {
        var harness = With(Bad());

        var result = await harness.Get().HandleAsync(new GetActionItemsQuery(new ActionItemsRequest(Guid.NewGuid(), null, null)), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("closed")]
    public async Task An_unknown_status_is_a_field_error(string status)
    {
        var result = await With(Bad()).Get().HandleAsync(new GetActionItemsQuery(new ActionItemsRequest(null, null, status)), CancellationToken.None);

        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("status");
    }

    [Fact]
    public async Task A_bucket_keeps_the_items_of_its_holdings_but_the_weights_stay_shares_of_the_whole_account()
    {
        // BIG is 5,000 of 7,200 (69.4 %) of the priced value of the account (three priced positions, limit 50 %; D has no price). Read alone, its bucket would make it 100 %: the weight shows which base was used.
        var holdings = new[] { Pos("BIG", 500m, bucket: BucketA), Pos("B", 110m, bucket: BucketB), Pos("C", 110m, bucket: BucketB), Pos("D", 0m, bucket: BucketB) };
        var harness = With(holdings);

        var inBucketA = await harness.ListAsync(bucket: BucketA);
        var inBucketB = await harness.ListAsync(bucket: BucketB);

        var item = inBucketA.Items.Single(i => i.Rule == "overweight");
        item.Numbers.WeightPct.ShouldBe(69.4444m);
        item.Numbers.Positions.ShouldBe(3);
        inBucketB.Items.ShouldBeEmpty();
        harness.Queries.LastFilter!.BucketId.ShouldBeNull();
        inBucketB.Counts.NoPrice.ShouldBe(1);
        inBucketA.Counts.NoPrice.ShouldBe(0);
    }

    [Fact]
    public async Task The_status_filter_chooses_the_items_and_the_counts_cover_the_whole_scope()
    {
        var harness = With(Bad(), Pos("WIN", 140m), Pos("UPPER", 150m));
        await harness.SnoozeAsync($"large_gain:{harness.Queries.Holdings[1].Id}");
        await harness.DoneAsync($"large_gain:{harness.Queries.Holdings[2].Id}");

        var open = await harness.ListAsync();
        var snoozed = await harness.ListAsync("snoozed");
        var done = await harness.ListAsync("done");
        var all = await harness.ListAsync("all");

        open.Items.Select(i => i.Symbol).ShouldBe(["BAD"]);
        snoozed.Items.Select(i => (i.Symbol, i.State)).ShouldBe([("WIN", "snoozed")]);
        done.Items.Select(i => (i.Symbol, i.State)).ShouldBe([("UPPER", "done")]);
        all.Items.Count.ShouldBe(3);
        foreach (var list in new[] { open, snoozed, done, all })
        {
            (list.Counts.Open, list.Counts.Snoozed, list.Counts.Done).ShouldBe((1, 1, 1));
        }

        open.Counts.ByRule["large_gain"].ShouldBe(0); // by rule and severity count the open items
    }

    [Fact]
    public async Task A_snooze_holds_until_its_date_and_the_item_is_open_again_on_that_day()
    {
        var harness = With(Bad());
        await harness.SnoozeAsync($"below_cost:{harness.Queries.Holdings[0].Id}");

        var today = await harness.ListAsync();
        harness.Advance(TimeSpan.FromDays(6));
        var sixDays = await harness.ListAsync();
        harness.Advance(TimeSpan.FromDays(1));
        var sevenDays = await harness.ListAsync();

        today.Items.ShouldBeEmpty();
        sixDays.Items.ShouldBeEmpty();
        sevenDays.Items.Select(i => i.State).ShouldBe(["open"]);
        sevenDays.Counts.Snoozed.ShouldBe(0);
    }

    [Fact]
    public async Task The_snoozed_item_says_when_it_shows_again()
    {
        var harness = With(Bad());
        await harness.SnoozeAsync($"below_cost:{harness.Queries.Holdings[0].Id}");

        var item = (await harness.ListAsync("snoozed")).Items.ShouldHaveSingleItem();

        item.SnoozedUntil.ShouldBe(new DateOnly(2026, 10, 19));
    }

    [Fact]
    public async Task A_changed_threshold_changes_the_next_read_at_once()
    {
        var harness = With(Pos("MID", 94m)); // -6 %
        (await harness.ListAsync()).Items.ShouldBeEmpty();

        var saved = await harness.Writer().WriteAsync(ActionThresholds.Defaults with { BelowCostPct = 5m }, ActionsHarness.Owner, reset: false, CancellationToken.None);

        saved.IsSuccess.ShouldBeTrue();
        (await harness.ListAsync()).Items.Select(i => (i.Symbol, i.Severity)).ShouldBe([("MID", "medium")]);
    }

    [Fact]
    public async Task The_read_makes_one_read_of_holdings_sectors_baskets_and_decisions_and_none_of_the_index_when_no_holding_is_old()
    {
        var harness = With(Bad(), Pos("WIN", 140m));

        await harness.ListAsync();

        (harness.Queries.Reads, harness.Classifications.SectorReads, harness.Classifications.BasketReads, harness.Data.StateReads).ShouldBe((1, 1, 1, 1));
        (harness.Prices.Calls, harness.Benchmarks.Instruments.Count).ShouldBe((0, 0));
    }

    [Fact]
    public async Task The_index_is_read_once_for_all_holdings_when_some_are_old_enough_for_the_lagging_rule()
    {
        var old = new DateOnly(2025, 1, 2);
        var harness = With(Pos("A", 100m, acquired: old), Pos("B", 100m, acquired: old), Pos("C", 100m, acquired: old));
        var nifty = harness.Benchmarks.Add("NSE:NIFTY", "Nifty 50");
        harness.Benchmarks.Default = nifty.Id;
        harness.Prices.Set(nifty.Id, [(old, 1000.0), (Today, 1300.0)]);

        var list = await harness.ListAsync();

        harness.Prices.Calls.ShouldBe(1);
        harness.Prices.Last!.Value.Ids.ShouldBe([nifty.Id]);
        list.Items.Select(i => i.Rule).ShouldBe(["lagging_nifty", "lagging_nifty", "lagging_nifty"]);
        harness.Queries.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Holdings_that_cannot_be_judged_against_the_index_are_counted_as_unavailable()
    {
        var harness = With(Pos("A", 100m, acquired: new DateOnly(2025, 1, 2)), Pos("YOUNG", 100m));

        var list = await harness.ListAsync(); // there is no benchmark index at all

        list.Items.ShouldBeEmpty();
        list.Counts.Unavailable.Lagging.ShouldBe(1);
    }

    [Fact]
    public async Task Open_holdings_without_a_price_are_counted_and_never_listed_by_a_price_rule()
    {
        var harness = With(Pos("NEW", 0m), Bad());

        var list = await harness.ListAsync();

        list.Counts.NoPrice.ShouldBe(1);
        list.Items.Select(i => i.Symbol).ShouldBe(["BAD"]);
    }

    [Fact]
    public async Task The_india_date_decides_a_long_term_window_not_the_utc_date()
    {
        // 18:45 UTC on 12 October is 00:15 on 13 October in India. A lot bought on 2025-11-12 turns 12 months old 30 days after 13 October, 31 after 12 October.
        var harness = With(Pos("LT", 150m, 10, 100m, new DateOnly(2025, 11, 12)));
        harness.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 18, 45, 0, TimeSpan.Zero));

        var list = await harness.ListAsync();

        list.Items.Select(i => i.Rule).ShouldContain("long_term_soon");
    }

    [Fact]
    public async Task A_closed_holding_has_no_items()
    {
        var closed = Pos("OLD", 50m) with { Status = "closed" };

        var list = await With(closed).ListAsync();

        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Severity_and_numbers_are_recomputed_on_every_read_while_the_id_stays()
    {
        var harness = With(Pos("SLIP", 88m)); // -12 %: medium
        var first = (await harness.ListAsync()).Items.Single();

        harness.Reprice("SLIP", 70m); // -30 %: high
        var second = (await harness.ListAsync()).Items.Single();

        (first.Severity, second.Severity, second.Id).ShouldBe(("medium", "high", first.Id));
        (first.Numbers.PnlPct, second.Numbers.PnlPct).ShouldBe((-12m, -30m));
    }
}
