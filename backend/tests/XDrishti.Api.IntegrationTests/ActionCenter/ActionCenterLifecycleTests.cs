using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Portfolio.Actions;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>
/// The decisions on real rows (P2-09, criteria 9 and 11): stored and audited, snooze/done/reopen, and a done decision that is cleared when its condition ends — by the read and by the quotes tick — so that the item is open again when the condition returns.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ActionCenterLifecycleTests(ApiFactory factory) : ActionCenterTestBase(factory)
{
    private async Task<ActionItem> BelowCostAsync() => (await ActionsAsync()).Items.Single(i => i is { Rule: "below_cost", Symbol: "AAA" });

    [Fact]
    public async Task A_decision_is_stored_and_survives_a_refresh_and_a_new_session()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");

        await DecidedAsync(item.Id, "done");

        (await StateOfAsync("below_cost", holding)).ShouldBe("done");
        var later = await Factory.SignInAsync(Ct);
        using var response = await later.Client.GetAsync("/api/portfolio/actions?status=done", Ct);
        (await response.ReadAsync<ActionList>(Ct)).Items.Select(i => i.Id).ShouldBe([item.Id]);
    }

    [Fact]
    public async Task Each_decision_writes_one_audit_event_with_the_actor_and_repeating_it_writes_none()
    {
        var item = await BelowCostAsync();

        await DecidedAsync(item.Id, "snooze");
        await DecidedAsync(item.Id, "snooze");
        await DecidedAsync(item.Id, "done");
        await DecidedAsync(item.Id, "done");
        await DecidedAsync(item.Id, "reopen");
        await DecidedAsync(item.Id, "reopen");

        (await EventsAsync("ActionSnoozed"), await EventsAsync("ActionDone"), await EventsAsync("ActionReopened")).ShouldBe((1, 1, 1));
        (await CountAsync($"SELECT count(*) FROM accounts.account_events WHERE event LIKE 'Action%' AND actor = '{ApiFactory.OwnerEmail}' AND account_id = '{Account.Id}'")).ShouldBe(3);
        (await TextAsync("SELECT detail FROM accounts.account_events WHERE event = 'ActionSnoozed'")).ShouldBe("Snoozed Below cost on AAA until 2026-10-19");
    }

    [Fact]
    public async Task Done_then_the_condition_ends_then_a_read_clears_it_and_when_it_returns_the_item_is_open_again_with_no_tick_between()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");
        await DecidedAsync(item.Id, "done");
        (await ActionsAsync()).Items.Any(i => i.Id == item.Id).ShouldBeFalse();

        await SetPriceAsync("AAA", 120m); // the price recovers
        var whileClear = await ActionsAsync("?status=all");
        var cleared = await StateOfAsync("below_cost", holding);
        await SetPriceAsync("AAA", 60m); // and falls again
        var after = await ActionsAsync();

        whileClear.Items.Any(i => i.Id == item.Id).ShouldBeFalse();
        cleared.ShouldBe("cleared");
        after.Items.Single(i => i.Id == item.Id).State.ShouldBe("open");
        after.Counts.Done.ShouldBe(0);
    }

    [Fact]
    public async Task The_quotes_tick_clears_the_decision_too_without_any_read()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");
        await DecidedAsync(item.Id, "done");
        Dhan.Quotes["1001"] = (120m, 100m);
        Dhan.Quotes["1002"] = (100m, 100m);
        Dhan.Quotes["1003"] = (140m, 140m);

        var run = await RunQuotesAsync();

        run.Status.ShouldBe("succeeded");
        (await StateOfAsync("below_cost", holding)).ShouldBe("cleared");
        (await TextAsync($"SELECT updated_by FROM portfolio.action_states WHERE key = '{holding}'")).ShouldBe("system:action-center");

        Dhan.Quotes["1001"] = (60m, 100m);
        await RunQuotesAsync();
        (await ActionsAsync()).Items.Single(i => i.Rule == "below_cost").State.ShouldBe("open");
    }

    [Fact]
    public async Task A_holdings_sync_clears_the_decision_too()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");
        await DecidedAsync(item.Id, "done");
        Dhan.Holdings.RemoveAll(h => h.Symbol == "AAA");
        Dhan.Holdings.Add(Holding("AAA", 100, 100m, 130m, "1001", "INE000A01011")); // the price recovered

        (await Client.SyncAsync(Account.Id, Ct)).Status.ShouldBe("succeeded");

        (await StateOfAsync("below_cost", holding)).ShouldBe("cleared");
    }

    [Fact]
    public async Task A_decision_stays_while_the_condition_holds_and_a_read_makes_no_write()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");
        await DecidedAsync(item.Id, "done");
        var stamp = await TextAsync($"SELECT updated_at::text FROM portfolio.action_states WHERE key = '{holding}'");

        await ActionsAsync();
        await ActionsAsync("?status=all");

        (await StateOfAsync("below_cost", holding)).ShouldBe("done");
        (await TextAsync($"SELECT updated_at::text FROM portfolio.action_states WHERE key = '{holding}'")).ShouldBe(stamp);
    }

    [Fact]
    public async Task A_snooze_ends_on_its_date()
    {
        var item = await BelowCostAsync();
        await DecidedAsync(item.Id, "snooze");
        (await ActionsAsync()).Items.Any(i => i.Id == item.Id).ShouldBeFalse();

        Factory.Clock.Pin(MondayMorning.AddDays(7));
        var later = await Factory.SignInAsync(Ct);
        using var response = await later.Client.GetAsync("/api/portfolio/actions", Ct);

        (await response.ReadAsync<ActionList>(Ct)).Items.Single(i => i.Id == item.Id).State.ShouldBe("open");
    }

    [Fact]
    public async Task A_decision_about_a_holding_that_is_sold_is_cleared_by_the_sync_so_the_same_holding_bought_again_at_a_loss_starts_open()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");
        await DecidedAsync(item.Id, "done");
        Dhan.Holdings.RemoveAll(h => h.Symbol == "AAA");
        (await Client.SyncAsync(Account.Id, Ct)).Status.ShouldBe("succeeded"); // AAA is one of three: not "most"

        var whileClosed = await ActionsAsync("?status=all");
        (await StateOfAsync("below_cost", holding)).ShouldBe("cleared");
        Dhan.Holdings.Add(Holding("AAA", 50, 100m, 60m, "1001", "INE000A01011")); // the same row is opened again by the next sync
        (await Client.SyncAsync(Account.Id, Ct)).Status.ShouldBe("succeeded");

        whileClosed.Items.Any(i => i.Symbol == "AAA").ShouldBeFalse();
        whileClosed.Counts.Done.ShouldBe(0);
        (await HoldingIdAsync("AAA")).ShouldBe(holding);
        (await ActionsAsync()).Items.Single(i => i is { Rule: "below_cost", Symbol: "AAA" }).State.ShouldBe("open");
    }

    [Fact]
    public async Task The_read_alone_does_not_clear_the_decision_about_a_holding_it_does_not_see()
    {
        var item = await BelowCostAsync();
        var holding = await HoldingIdAsync("AAA");
        await DecidedAsync(item.Id, "done");
        await ExecuteAsync("UPDATE portfolio.holdings SET status = 'closed', closed_at = now() WHERE trading_symbol = 'AAA'");

        var list = await ActionsAsync("?status=all");

        list.Items.Any(i => i.Symbol == "AAA").ShouldBeFalse();
        (await StateOfAsync("below_cost", holding)).ShouldBe("done"); // the next tick or sync decides
    }
}
