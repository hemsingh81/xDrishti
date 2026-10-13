using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>
/// Snooze, done and reopen (acceptance criteria 9 and 11): the decisions, their audit events, idempotence, stale ids, and the life of a done decision — it hides the item while the condition holds, is cleared when the condition stops holding
/// (by the read and by the tick) and the item is open again when the condition returns. Monday 2026-10-12 10:00 India time.
/// </summary>
public sealed class ActionDecisionTests
{
    private static ActionsHarness Harness(out HoldingData bad)
    {
        bad = Pos("BAD", 70m); // -30 %: below cost, high
        var harness = new ActionsHarness(bad);
        harness.Classifications.Baskets[bad.InstrumentId!.Value] = [new BasketRef(Guid.NewGuid(), "Core")];
        return harness;
    }

    private static string IdOf(HoldingData holding) => $"below_cost:{holding.Id}";

    [Fact]
    public async Task Snooze_hides_the_item_for_seven_days_and_audits_it()
    {
        var harness = Harness(out var bad);

        var result = await harness.SnoozeAsync(IdOf(bad));

        var view = result.Value;
        (view.Id, view.State, view.SnoozedUntil).ShouldBe((IdOf(bad), "snoozed", new DateOnly(2026, 10, 19)));
        (view.Item!.State, view.Item.SnoozedUntil).ShouldBe(("snoozed", new DateOnly(2026, 10, 19)));
        var audit = harness.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.AccountId, audit.Actor).ShouldBe((AccountEventType.ActionSnoozed, AccountOne, ActionsHarness.Owner));
        audit.Detail.ShouldBe("Snoozed Below cost on BAD until 2026-10-19");
        (await harness.ListAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_seven_days_are_counted_on_the_india_date()
    {
        var harness = Harness(out var bad);
        harness.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 19, 0, 0, TimeSpan.Zero)); // 00:30 on 13 October in India

        var view = (await harness.SnoozeAsync(IdOf(bad))).Value;

        view.SnoozedUntil.ShouldBe(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public async Task Done_hides_the_item_while_the_condition_holds_and_audits_it()
    {
        var harness = Harness(out var bad);

        var view = (await harness.DoneAsync(IdOf(bad))).Value;

        (view.State, view.SnoozedUntil, view.Item!.State).ShouldBe(("done", null, "done"));
        var audit = harness.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.ActionDone, "Marked done Below cost on BAD"));
        var done = await harness.ListAsync("done");
        done.Items.Select(i => i.Id).ShouldBe([IdOf(bad)]);
        (await harness.ListAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_decision_survives_a_refresh_it_is_stored()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));

        var stored = harness.Data.StateOf("below_cost", bad.Id.ToString())!;

        (stored.State, stored.UpdatedBy).ShouldBe((ActionDecision.Done, ActionsHarness.Owner));
        (await harness.ListAsync("all")).Items.Single().State.ShouldBe("done");
    }

    [Fact]
    public async Task Repeating_a_decision_changes_and_records_nothing()
    {
        var harness = Harness(out var bad);
        await harness.SnoozeAsync(IdOf(bad));
        await harness.DoneAsync(IdOf(bad));

        var again = await harness.DoneAsync(IdOf(bad));
        var snoozeAgain = await harness.SnoozeAsync(IdOf(bad)); // done -> snoozed is a change
        var snoozeTwice = await harness.SnoozeAsync(IdOf(bad)); // the same day: not

        again.IsSuccess.ShouldBeTrue();
        snoozeAgain.IsSuccess.ShouldBeTrue();
        snoozeTwice.IsSuccess.ShouldBeTrue();
        harness.Data.Events.Select(e => e.Event).ShouldBe([AccountEventType.ActionSnoozed, AccountEventType.ActionDone, AccountEventType.ActionSnoozed]);
    }

    [Fact]
    public async Task Snoozing_again_on_a_later_day_extends_the_snooze()
    {
        var harness = Harness(out var bad);
        await harness.SnoozeAsync(IdOf(bad));
        harness.Advance(TimeSpan.FromDays(2));

        var view = (await harness.SnoozeAsync(IdOf(bad))).Value;

        view.SnoozedUntil.ShouldBe(new DateOnly(2026, 10, 21));
        harness.Data.Events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Reopen_takes_either_decision_back_and_audits_it()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));

        var view = (await harness.ReopenAsync(IdOf(bad))).Value;

        (view.State, view.SnoozedUntil, view.Item!.State).ShouldBe(("open", null, "open"));
        harness.Data.Events.Select(e => (e.Event, e.AccountId)).ShouldBe([(AccountEventType.ActionDone, AccountOne), (AccountEventType.ActionReopened, AccountOne)]);
        (await harness.ListAsync()).Items.Select(i => i.Id).ShouldBe([IdOf(bad)]);

        await harness.SnoozeAsync(IdOf(bad));
        (await harness.ReopenAsync(IdOf(bad))).Value.State.ShouldBe("open");
        (await harness.ListAsync()).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reopening_an_item_with_no_decision_is_a_no_op()
    {
        var harness = Harness(out var bad);

        var result = await harness.ReopenAsync(IdOf(bad));

        result.IsSuccess.ShouldBeTrue();
        harness.Data.Events.ShouldBeEmpty();
        harness.Data.AllStates.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("snooze")]
    [InlineData("done")]
    public async Task A_stale_id_that_no_rule_produces_now_is_not_found(string decision)
    {
        var harness = Harness(out _);
        var stale = $"below_cost:{Guid.NewGuid()}";

        var result = decision == "snooze" ? await harness.SnoozeAsync(stale) : await harness.DoneAsync(stale);

        (result.Error.Code, result.Error.Kind).ShouldBe(("portfolio.not_found", XDrishti.Domain.Common.ErrorKind.NotFound));
        harness.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_whose_condition_ended_cannot_be_snoozed_but_can_be_reopened()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Reprice("BAD", 120m); // the condition ends

        (await harness.SnoozeAsync(IdOf(bad))).Error.Code.ShouldBe("portfolio.not_found");
        var reopened = await harness.ReopenAsync(IdOf(bad));

        reopened.IsSuccess.ShouldBeTrue();
        (reopened.Value.State, reopened.Value.Item).ShouldBe(("open", null));
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Cleared);
        harness.Data.Events.Last().Event.ShouldBe(AccountEventType.ActionReopened);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("unknown_rule:11111111-2222-7333-8444-555555555555")]
    [InlineData("below_cost:not-a-guid")]
    public async Task An_id_that_is_not_the_id_of_any_item_is_not_found_even_for_reopen(string id)
    {
        var harness = Harness(out _);

        (await harness.SnoozeAsync(id)).Error.Code.ShouldBe("portfolio.not_found");
        (await harness.DoneAsync(id)).Error.Code.ShouldBe("portfolio.not_found");
        (await harness.ReopenAsync(id)).Error.Code.ShouldBe("portfolio.not_found");
    }

    [Fact]
    public async Task A_concentration_item_is_decided_by_its_account_and_sector_id()
    {
        var holdings = new[] { Pos("A", 300m), Pos("B", 100m), Pos("C", 300m), Pos("D", 300m) };
        var harness = new ActionsHarness(holdings);
        foreach (var holding in holdings)
        {
            harness.Classifications.Baskets[holding.InstrumentId!.Value] = [new BasketRef(Guid.NewGuid(), "Core")];
        }

        harness.Classifications.Sectors[InstrumentOf("A")] = "Banks";
        harness.Classifications.Sectors[InstrumentOf("B")] = "Banks";
        var id = (await harness.ListAsync()).Items.Single(i => i.Rule == "sector_concentration").Id;

        var done = await harness.DoneAsync(id);

        done.Value.Item!.State.ShouldBe("done");
        harness.Data.Events.Single().AccountId.ShouldBe(AccountOne);
        harness.Data.Events.Single().Detail.ShouldBe("Marked done Sector concentration (Banks) in Account One");
        (await harness.ListAsync()).Items.Any(i => i.Rule == "sector_concentration").ShouldBeFalse();
    }

    [Fact]
    public async Task A_save_that_collides_with_a_concurrent_change_is_a_conflict_and_stores_nothing()
    {
        var harness = Harness(out var bad);
        harness.Data.RefuseNextSave = true;

        var result = await harness.DoneAsync(IdOf(bad));

        (result.Error.Code, result.Error.Kind).ShouldBe(("portfolio.conflict", XDrishti.Domain.Common.ErrorKind.Conflict));
        harness.Data.AllStates.ShouldBeEmpty();
        harness.Data.Events.ShouldBeEmpty();
    }

    // ---- the life of a done decision ----

    [Fact]
    public async Task Done_then_the_condition_clears_then_the_read_clears_the_decision_and_when_it_returns_the_item_is_open_again()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        (await harness.ListAsync()).Items.ShouldBeEmpty();

        harness.Reprice("BAD", 120m); // no tick in between: only the reads
        var whileClear = await harness.ListAsync("all");
        harness.Reprice("BAD", 65m); // the condition returns
        var after = await harness.ListAsync();

        whileClear.Items.Any(i => i.Rule == "below_cost").ShouldBeFalse();
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Cleared);
        after.Items.Single(i => i.Rule == "below_cost").State.ShouldBe("open");
        after.Counts.Done.ShouldBe(0);
    }

    [Fact]
    public async Task The_tick_clears_the_decision_the_same_way_even_without_a_read()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Reprice("BAD", 120m);

        var cleared = await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);
        harness.Reprice("BAD", 65m);

        cleared.ShouldBe(1);
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.UpdatedBy.ShouldBe(ActionReconciler.Actor);
        (await harness.ListAsync()).Items.Single(i => i.Rule == "below_cost").State.ShouldBe("open");
    }

    [Fact]
    public async Task While_the_condition_holds_nothing_is_cleared_and_no_write_is_made_by_a_read()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        var saves = harness.Data.Saves;
        var before = harness.Data.StateOf("below_cost", bad.Id.ToString())!.UpdatedAt;

        await harness.ListAsync();
        await harness.ListAsync("all");

        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Done);
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.UpdatedAt.ShouldBe(before);
        harness.Data.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_snooze_is_not_cleared_by_the_condition_ending()
    {
        var harness = Harness(out var bad);
        await harness.SnoozeAsync(IdOf(bad));
        harness.Reprice("BAD", 120m);

        await harness.ListAsync();
        await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);

        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Snoozed);
    }

    [Fact]
    public async Task A_done_decision_about_a_condition_that_could_not_be_judged_is_kept()
    {
        // Held long enough for the lagging rule, but the index has no bars: the rule is not evaluated, so its decision must not be dropped.
        var old = Pos("OLD", 100m, acquired: new DateOnly(2025, 1, 2));
        var harness = new ActionsHarness(old);
        harness.Classifications.Baskets[old.InstrumentId!.Value] = [new BasketRef(Guid.NewGuid(), "Core")];
        var nifty = harness.Benchmarks.Add("NSE:NIFTY", "Nifty 50");
        harness.Benchmarks.Default = nifty.Id;
        harness.Prices.Set(nifty.Id, [(old.Lots[0].AcquiredOn, 1000.0), (Today, 1300.0)]);
        var id = (await harness.ListAsync()).Items.Single().Id;
        await harness.DoneAsync(id);

        harness.Prices.Stored.Clear(); // the index bars are gone
        await harness.ListAsync();
        await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);

        harness.Data.StateOf("lagging_nifty", old.Id.ToString())!.State.ShouldBe(ActionDecision.Done);
    }

    [Fact]
    public async Task A_done_price_decision_is_kept_while_the_holding_has_no_price()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Reprice("BAD", 0m); // a quote glitch: the price is unknown, not "recovered"

        await harness.ListAsync();
        await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);

        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Done);
    }

    [Fact]
    public async Task The_read_ignores_and_keeps_the_decisions_about_a_holding_it_does_not_see()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Queries.Holdings[0] = bad with { Status = "closed" };

        var list = await harness.ListAsync("all");

        list.Items.ShouldBeEmpty();
        list.Counts.Done.ShouldBe(0);
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Done); // the read cannot tell a closed holding from one outside its scope
    }

    [Fact]
    public async Task The_tick_and_the_sync_clear_the_decision_about_a_holding_that_is_no_longer_open_so_a_reopened_holding_starts_open()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Queries.Holdings[0] = bad with { Status = "closed" }; // sold

        var cleared = await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);
        harness.Queries.Holdings[0] = bad with { LastPrice = 65m }; // the same holding row is opened again by a sync, at a loss

        cleared.ShouldBe(1);
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Cleared);
        (await harness.ListAsync()).Items.Single(i => i.Rule == "below_cost").State.ShouldBe("open");
    }

    [Fact]
    public async Task The_full_scope_clears_a_concentration_decision_of_an_account_without_open_holdings_and_the_read_does_not()
    {
        var harness = Harness(out _);
        var key = ActionKey.ForSector(AccountOne, "Banks");
        harness.Data.SeedState(key.Rule, key.Key, ActionDecision.Done, null, harness.Now);
        harness.Queries.Holdings.Clear();

        await harness.ListAsync("all");
        harness.Data.StateOf(key.Rule, key.Key)!.State.ShouldBe(ActionDecision.Done);
        await harness.Reconciler().ReconcileAsync("sync", CancellationToken.None);

        harness.Data.StateOf(key.Rule, key.Key)!.State.ShouldBe(ActionDecision.Cleared);
    }

    [Fact]
    public async Task A_decision_made_again_meanwhile_is_not_cleared_by_an_older_evaluation()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Reprice("BAD", 120m);
        var evaluation = await harness.Evaluator().EvaluateAsync(null, false, CancellationToken.None); // reads the done decision as it is now
        harness.Advance(TimeSpan.FromMinutes(5));
        await harness.ReopenAsync(IdOf(bad)); // the owner reopens ...
        harness.Reprice("BAD", 65m);
        await harness.DoneAsync(IdOf(bad)); // ... and decides again, later than the evaluation read

        var cleared = await harness.Reconciler().ClearStaleAsync(evaluation, "read", CancellationToken.None);

        cleared.ShouldBe(0);
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Done);
    }

    [Fact]
    public async Task A_failure_while_clearing_never_fails_the_read_or_the_tick()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Reprice("BAD", 120m);
        harness.Data.ThrowOnClear = true;

        var list = await harness.ListAsync("all");
        var cleared = await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);

        list.Items.Any(i => i.Rule == "below_cost").ShouldBeFalse();
        cleared.ShouldBe(0);
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Done);
    }

    [Fact]
    public async Task The_tick_does_no_work_without_a_done_decision()
    {
        var harness = Harness(out _);

        var cleared = await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);

        cleared.ShouldBe(0);
        harness.Queries.Reads.ShouldBe(0); // the holdings were not even read
    }

    [Fact]
    public async Task The_tick_judges_every_portfolio_account_not_only_those_that_count_in_totals()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));
        harness.Reprice("BAD", 120m);

        await harness.Reconciler().ReconcileAsync("quotes", CancellationToken.None);

        var filter = harness.Queries.LastFilter!;
        (filter.AccountId, filter.TotalsOnly).ShouldBe((null, false));
    }

    // ---- a concurrent request, thresholds ----

    [Fact]
    public async Task A_concurrent_request_that_stored_the_same_decision_first_makes_this_one_succeed_not_conflict()
    {
        var harness = Harness(out var bad);
        harness.Data.RefuseNextSave = true;
        harness.Data.OnRefusedSave = () => harness.Data.SeedState("below_cost", bad.Id.ToString(), ActionDecision.Done, null, harness.Now);

        var result = await harness.DoneAsync(IdOf(bad));

        result.IsSuccess.ShouldBeTrue();
        (result.Value.State, result.Value.Item!.State).ShouldBe(("done", "done"));
        harness.Data.Events.ShouldBeEmpty(); // the other request recorded its own
    }

    [Fact]
    public async Task A_concurrent_snooze_of_the_same_day_is_the_same_decision_but_a_different_one_is_a_conflict()
    {
        var harness = Harness(out var bad);
        harness.Data.RefuseNextSave = true;
        harness.Data.OnRefusedSave = () => harness.Data.SeedState("below_cost", bad.Id.ToString(), ActionDecision.Snoozed, new DateOnly(2026, 10, 19), harness.Now);
        (await harness.SnoozeAsync(IdOf(bad))).IsSuccess.ShouldBeTrue();

        var other = Harness(out var bad2);
        other.Data.RefuseNextSave = true;
        other.Data.OnRefusedSave = () => other.Data.SeedState("below_cost", bad2.Id.ToString(), ActionDecision.Done, null, other.Now);
        var conflict = await other.SnoozeAsync(IdOf(bad2));

        conflict.Error.Code.ShouldBe("portfolio.conflict");
    }

    [Fact]
    public async Task Changing_a_threshold_clears_the_done_decision_the_new_value_ended_so_lowering_it_again_brings_the_item_back_open()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad)); // -30 %: high below cost
        var raised = ActionThresholds.Defaults with { BelowCostPct = 40m, BelowCostHighPct = 50m };

        await harness.Writer().WriteAsync(raised, ActionsHarness.Owner, reset: false, CancellationToken.None); // no read, no tick
        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Cleared);
        await harness.Writer().WriteAsync(ActionThresholds.Defaults, ActionsHarness.Owner, reset: true, CancellationToken.None);

        (await harness.ListAsync()).Items.Single(i => i.Rule == "below_cost").State.ShouldBe("open");
    }

    [Fact]
    public async Task A_threshold_change_that_leaves_the_item_in_place_keeps_its_decision()
    {
        var harness = Harness(out var bad);
        await harness.DoneAsync(IdOf(bad));

        await harness.Writer().WriteAsync(ActionThresholds.Defaults with { GainPct = 50m }, ActionsHarness.Owner, reset: false, CancellationToken.None);

        harness.Data.StateOf("below_cost", bad.Id.ToString())!.State.ShouldBe(ActionDecision.Done);
    }
}
