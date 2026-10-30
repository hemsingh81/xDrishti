using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Planning;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Intents;

/// <summary>
/// Spec P12-08 criterion 16 (P12-08a) on fakes: an approved ticket with an allocation for a semi_auto or auto_live account becomes exactly one intent, planned then armed; a second pass changes nothing; no other mode gets one; a crossing of the
/// trigger inside the window with the account ready and no halt moves it to checking once, and anything else cancels it with the reason (no catch-up); a trigger already run past the collar is void.
/// </summary>
public sealed class IntentCreatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IntentScene> ArmedAsync()
    {
        var scene = new IntentScene();
        await scene.Creator().RunOnceAsync(Ct);
        scene.ItemState(TicketState.Armed);
        scene.Now(27, 9, 10);
        await scene.Creator().RunOnceAsync(Ct);
        scene.Single().State.ShouldBe(ExecState.Armed);
        return scene;
    }

    [Fact]
    public async Task An_approved_ticket_of_a_semi_auto_account_becomes_one_planned_intent_with_its_links_and_a_second_pass_changes_nothing()
    {
        var scene = new IntentScene();

        var first = await scene.Creator().RunOnceAsync(Ct);
        var second = await scene.Creator().RunOnceAsync(Ct);

        first.ShouldNotBeNull().Created.ShouldBe(1);
        second.ShouldNotBeNull().ShouldBe(new(0, 0, 0, 0, 0));
        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Planned);
        intent.PlanItemId.ShouldBe(501);
        intent.ModeTrailId.ShouldBe(41);
        intent.Entry.ShouldBe(412.35m);
        intent.Session.ShouldBe(Ist.Session);
        scene.Stored(intent).ShouldBe((77L, (long?)9001, 120, "3426"));
        scene.EventCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_auto_live_account_gets_an_intent_too()
    {
        var scene = new IntentScene();
        scene.Modes[IntentScene.Account] = new ModeAnswer(ExecMode.AutoLive, true);

        await scene.Creator().RunOnceAsync(Ct);

        scene.Single().State.ShouldBe(ExecState.Planned);
    }

    [Theory]
    [InlineData(ExecMode.Paper)]
    [InlineData(ExecMode.ManualLive)]
    [InlineData(ExecMode.Assisted)]
    public async Task A_paper_manual_live_or_assisted_account_gets_no_intent(ExecMode mode)
    {
        var scene = new IntentScene();
        scene.Modes[IntentScene.Account] = new ModeAnswer(mode, true);

        var pass = await scene.Creator().RunOnceAsync(Ct);

        scene.Intents.ShouldBeEmpty();
        pass.ShouldNotBeNull().Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task No_mode_answer_the_switch_off_or_environment_none_creates_nothing()
    {
        var closed = new IntentScene();
        closed.Modes[IntentScene.Account] = null;
        var off = new IntentScene();
        off.Modes[IntentScene.Account] = new ModeAnswer(ExecMode.SemiAuto, false);
        var none = new IntentScene();

        await closed.Creator().RunOnceAsync(Ct);
        await off.Creator().RunOnceAsync(Ct);
        await none.Creator(ExecEnvironment.None).RunOnceAsync(Ct);

        closed.Intents.ShouldBeEmpty();
        off.Intents.ShouldBeEmpty();
        none.Intents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sell_ticket_a_ticket_without_numeric_levels_and_an_unknown_instrument_get_no_intent()
    {
        var scene = new IntentScene();
        scene.Candidates.Clear();
        scene.Candidates.AddRange([IntentScene.Ticket(601, side: "SELL"), IntentScene.Ticket(602, entry: null), IntentScene.Ticket(603, security: null), IntentScene.Ticket(604, quantity: 0)]);

        var pass = await scene.Creator().RunOnceAsync(Ct);

        scene.Intents.ShouldBeEmpty();
        pass.ShouldNotBeNull().Skipped.ShouldBe(4);
    }

    [Theory]
    [InlineData(TicketState.Proposed)]
    [InlineData(TicketState.Modified)]
    [InlineData(TicketState.Invalidated)]
    [InlineData(TicketState.Expired)]
    [InlineData(TicketState.Triggered)]
    public async Task Only_an_approved_or_armed_ticket_becomes_an_intent(TicketState state)
    {
        var scene = new IntentScene();
        scene.ItemState(state);

        await scene.Creator().RunOnceAsync(Ct);

        scene.Intents.ShouldBeEmpty();
    }

    [Fact]
    public async Task While_the_canary_is_pending_the_intent_holds_one_share()
    {
        var scene = new IntentScene();
        scene.Settings = scene.Settings! with { CanaryPending = true };

        await scene.Creator().RunOnceAsync(Ct);

        scene.Stored(scene.Single()).Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task Without_settings_a_readable_plan_record_or_readable_intents_the_pass_does_nothing()
    {
        var noSettings = new IntentScene { Settings = null };
        var unreadable = new IntentScene { PlanUnreadable = true };
        var noIntents = new IntentScene { IntentsUnreadable = true };

        (await noSettings.Creator().RunOnceAsync(Ct)).ShouldBeNull();
        (await unreadable.Creator().RunOnceAsync(Ct)).ShouldBeNull();
        (await noIntents.Creator().RunOnceAsync(Ct)).ShouldBeNull();

        noSettings.Intents.ShouldBeEmpty();
        unreadable.Intents.ShouldBeEmpty();
        noIntents.IntentsUnreadable = false;
        noIntents.Intents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_planned_intent_is_armed_once_its_ticket_is_armed_and_not_before_09_10()
    {
        var scene = new IntentScene();
        await scene.Creator().RunOnceAsync(Ct);
        scene.ItemState(TicketState.Armed);

        scene.Now(27, 9, 9, 59);
        await scene.Creator().RunOnceAsync(Ct);
        var before = scene.Single().State;
        scene.Now(27, 9, 10);
        var armed = await scene.Creator().RunOnceAsync(Ct);
        var again = await scene.Creator().RunOnceAsync(Ct);

        before.ShouldBe(ExecState.Planned);
        armed.ShouldNotBeNull().Armed.ShouldBe(1);
        again.ShouldNotBeNull().Armed.ShouldBe(0);
        scene.Single().State.ShouldBe(ExecState.Armed);
        scene.EventCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_planned_intent_whose_ticket_is_only_approved_is_not_armed()
    {
        var scene = new IntentScene();
        await scene.Creator().RunOnceAsync(Ct);
        scene.Now(27, 9, 15);

        await scene.Creator().RunOnceAsync(Ct);

        scene.Single().State.ShouldBe(ExecState.Planned);
    }

    [Fact]
    public async Task The_first_crossing_inside_the_window_while_ready_and_not_halted_moves_the_intent_to_checking_once()
    {
        var scene = await ArmedAsync();
        scene.Now(27, 10, 0);
        scene.PriceNow(412.30m);
        var below = await scene.Creator().RunOnceAsync(Ct);

        scene.PriceNow(412.35m);
        var crossed = await scene.Creator().RunOnceAsync(Ct);
        scene.PriceNow(413m);
        var later = await scene.Creator().RunOnceAsync(Ct);

        below.ShouldNotBeNull().Checking.ShouldBe(0);
        crossed.ShouldNotBeNull().Checking.ShouldBe(1);
        later.ShouldNotBeNull().Checking.ShouldBe(0);
        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Checking);
        scene.LastReason(intent).ShouldBe(IntentReasons.Triggered);
    }

    public static TheoryData<string> NotReadyCases => ["not_ready", "unavailable", "none", "stale", "other_session"];

    [Theory]
    [MemberData(nameof(NotReadyCases))]
    public async Task A_crossing_while_the_account_is_not_ready_cancels_the_intent_missed_not_ready(string kind)
    {
        var scene = await ArmedAsync();
        scene.Now(27, 10, 0);
        var ready = scene.Readiness!;
        scene.Readiness = kind switch
        {
            "not_ready" => ready with { Verdict = ReadinessState.NotReady, At = Ist.At(27, 9, 59) },
            "unavailable" => ready with { Verdict = ReadinessState.Unavailable, At = Ist.At(27, 9, 59) },
            "none" => null,
            "stale" => ready with { At = Ist.At(27, 9, 43, 59) },
            _ => ready with { SessionDate = Ist.Session.AddDays(-1), At = Ist.At(27, 9, 59) },
        };
        scene.PriceNow(413m);

        await scene.Creator().RunOnceAsync(Ct);

        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Cancelled);
        scene.LastReason(intent).ShouldBe(IntentReasons.MissedNotReady);
    }

    [Fact]
    public async Task A_readiness_check_16_minutes_old_still_counts()
    {
        var scene = await ArmedAsync();
        scene.Now(27, 10, 0);
        scene.Readiness = scene.Readiness! with { At = Ist.At(27, 9, 44) };
        scene.PriceNow(413m);

        await scene.Creator().RunOnceAsync(Ct);

        scene.Single().State.ShouldBe(ExecState.Checking);
    }

    [Theory]
    [InlineData(HaltReading.Halted)]
    [InlineData(HaltReading.Unreadable)]
    public async Task A_crossing_under_a_halt_or_an_unreadable_halt_record_cancels_the_intent_halted(HaltReading halt)
    {
        var scene = await ArmedAsync();
        scene.Now(27, 10, 0);
        scene.Halt = halt;
        scene.Readiness = null; // the strongest cause is named
        scene.PriceNow(413m);

        await scene.Creator().RunOnceAsync(Ct);

        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Cancelled);
        scene.LastReason(intent).ShouldBe(IntentReasons.Halted);
    }

    [Fact]
    public async Task A_crossing_after_the_account_was_demoted_cancels_the_intent_mode_changed()
    {
        var scene = await ArmedAsync();
        scene.Now(27, 10, 0);
        scene.Modes[IntentScene.Account] = new ModeAnswer(ExecMode.Assisted, true);
        scene.PriceNow(413m);

        await scene.Creator().RunOnceAsync(Ct);

        scene.LastReason(scene.Single()).ShouldBe(IntentReasons.ModeChanged);
    }

    [Theory]
    [InlineData(9, 19, 59, ExecState.Cancelled)]
    [InlineData(9, 20, 0, ExecState.Checking)]
    [InlineData(15, 0, 0, ExecState.Checking)]
    public async Task The_entry_window_is_09_20_to_15_00_both_included_and_a_crossing_before_it_is_not_caught_up(int hour, int minute, int second, ExecState expected)
    {
        var scene = await ArmedAsync();
        scene.Now(27, hour, minute, second);
        scene.Readiness = scene.Readiness! with { At = scene.Clock.GetUtcNow() };
        scene.PriceNow(413m);

        await scene.Creator().RunOnceAsync(Ct);

        var intent = scene.Single();
        intent.State.ShouldBe(expected);
        scene.LastReason(intent).ShouldBe(expected == ExecState.Cancelled ? IntentReasons.Window : IntentReasons.Triggered);
    }

    [Fact]
    public async Task An_armed_intent_that_never_crossed_is_cancelled_window_once_the_window_has_closed()
    {
        var scene = await ArmedAsync();
        scene.Now(27, 15, 0, 1);

        await scene.Creator().RunOnceAsync(Ct);

        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Cancelled);
        scene.LastReason(intent).ShouldBe(IntentReasons.Window);
    }

    [Fact]
    public async Task A_stale_price_moves_nothing()
    {
        var scene = await ArmedAsync();
        scene.Now(27, 10, 0);
        scene.Price = new(413m, scene.Clock.GetUtcNow().AddSeconds(-6));

        await scene.Creator().RunOnceAsync(Ct);

        scene.Single().State.ShouldBe(ExecState.Armed);
    }

    [Theory]
    [InlineData(TicketState.Invalidated, IntentReasons.PlanInvalidated)]
    [InlineData(TicketState.Expired, IntentReasons.PlanExpired)]
    public async Task A_ticket_invalidated_or_expired_cancels_its_open_intent(TicketState state, string reason)
    {
        var scene = await ArmedAsync();
        scene.ItemState(state);

        await scene.Creator().RunOnceAsync(Ct);

        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Cancelled);
        scene.LastReason(intent).ShouldBe(reason);
    }

    [Fact]
    public async Task A_trigger_already_run_past_the_collar_when_the_intent_is_created_is_cancelled_void()
    {
        var scene = new IntentScene();
        scene.ItemState(TicketState.Armed);
        scene.Now(27, 9, 30);
        scene.PriceNow(421m); // (421 - 412.35) / 421 = 2.05 % > 2 %

        await scene.Creator().RunOnceAsync(Ct);

        var intent = scene.Single();
        intent.State.ShouldBe(ExecState.Cancelled);
        scene.LastReason(intent).ShouldBe(IntentReasons.Void);
    }

    [Fact]
    public async Task A_trigger_crossed_but_inside_the_collar_at_creation_is_not_void()
    {
        var scene = new IntentScene();
        scene.ItemState(TicketState.Armed);
        scene.Now(27, 9, 30);
        scene.PriceNow(420m); // 1.82 %

        await scene.Creator().RunOnceAsync(Ct);

        scene.Single().State.ShouldBe(ExecState.Planned);
    }

    [Fact]
    public async Task Another_account_without_a_sending_mode_gets_nothing_while_the_semi_auto_one_gets_its_intent()
    {
        var other = Guid.NewGuid();
        var scene = new IntentScene();
        scene.Candidates.Add(IntentScene.Ticket(account: other));
        scene.Modes[other] = new ModeAnswer(ExecMode.ManualLive, true);

        await scene.Creator().RunOnceAsync(Ct);

        scene.Single().AccountId.ShouldBe(IntentScene.Account);
    }

    [Fact]
    public void The_settings_cache_serves_the_registry_defaults_to_the_creator_and_nothing_once_the_read_is_stale()
    {
        var clock = new FakeTimeProvider(Ist.At(27, 9, 0));
        var cache = new ExecSettingsCache(clock);
        var view = new ExecSettingsView(ExecSettingRegistry.All.ToDictionary(x => x.Key, x => x.Default), []);

        cache.Set(view);
        var fresh = ((IIntentSettings)cache).Current();
        clock.Advance(ExecSettingsCache.MaxAge + TimeSpan.FromSeconds(1));
        var stale = ((IIntentSettings)cache).Current();

        fresh.ShouldBe(new IntentSettings(new TimeOnly(9, 20), new TimeOnly(15, 0), 2.0m, true, TimeSpan.FromSeconds(5)));
        stale.ShouldBeNull();
    }
}
