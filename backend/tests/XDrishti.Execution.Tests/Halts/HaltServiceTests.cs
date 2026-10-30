using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Halts;

/// <summary>P12-05 criteria 5, 6 and 7: a halt appears exactly once, never stacks, is read as halted by the gate, an unreadable record is halted, and nothing here can release a halt.</summary>
public sealed class HaltServiceTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 4, 0, 0, TimeSpan.Zero));
    private readonly InMemoryHaltStore _store = new();

    private HaltService Service() => new(_store, _clock);

    [Fact]
    public async Task A_request_writes_one_halt_and_a_repeat_of_the_same_trigger_and_scope_does_not_stack()
    {
        var service = Service();

        await service.RequestAsync(Account, HaltLevel.Entries, HaltTriggers.ProtectionBreach, "4 of 10 shares", CancellationToken.None);
        await service.RequestAsync(Account, HaltLevel.Entries, HaltTriggers.ProtectionBreach, "4 of 10 shares", CancellationToken.None);

        _store.Rows.ShouldBe(1);
        (await service.ReadAsync(CancellationToken.None)).ShouldBe(HaltReading.Halted);
    }

    [Fact]
    public async Task A_stronger_level_of_the_same_trigger_is_a_new_row_a_milder_one_is_not_and_the_strongest_wins()
    {
        var service = Service();
        await service.RequestAsync(Account, HaltLevel.Entries, HaltTriggers.DisasterLoss, "x", CancellationToken.None);

        await service.RequestAsync(Account, HaltLevel.ExitAll, HaltTriggers.DisasterLoss, "x", CancellationToken.None);
        await service.RequestAsync(Account, HaltLevel.Entries, HaltTriggers.DisasterLoss, "x", CancellationToken.None);

        var active = (await service.ActiveAsync(CancellationToken.None))!;
        active.Count.ShouldBe(2, "the cancel-level row (the trigger's own minimum) and the exit_all row");
        HaltRules.Effective(active, Account).ShouldBe(HaltLevel.ExitAll);
    }

    [Fact]
    public async Task A_trigger_never_asks_for_less_than_its_own_row_and_no_account_means_a_system_halt()
    {
        var service = Service();

        await service.RequestAsync(null, HaltLevel.Entries, HaltTriggers.RunawayRate, "7 sends", CancellationToken.None);

        var halt = (await service.ActiveAsync(CancellationToken.None))!.Single();
        (halt.Scope, halt.AccountId, halt.Level, halt.Source, halt.Actor).ShouldBe((HaltScope.System, null, HaltLevel.Cancel, "system", HaltService.SystemActor));
    }

    [Fact]
    public async Task An_unknown_code_and_the_owners_button_through_the_system_door_are_refused()
    {
        var service = Service();

        await Should.ThrowAsync<ArgumentException>(() => service.RequestAsync(Account, HaltLevel.Entries, "made_up", "x", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => service.RequestAsync(Account, HaltLevel.Entries, HaltTriggers.OwnerButton, "x", CancellationToken.None));
        _store.Rows.ShouldBe(0);
    }

    [Theory]
    [InlineData(HaltLevel.ExitAll, "protection_breach")]
    [InlineData(HaltLevel.ExitAll, "feed_loss")]
    [InlineData(HaltLevel.Freeze, "protection_breach")]
    [InlineData(HaltLevel.Freeze, "daily_loss")]
    public async Task Exit_all_and_freeze_are_asked_for_only_by_their_own_triggers(HaltLevel level, string code)
    {
        await Should.ThrowAsync<ArgumentException>(() => Service().RequestAsync(Account, level, code, "x", CancellationToken.None));

        _store.Rows.ShouldBe(0);
    }

    [Theory]
    [InlineData(HaltLevel.ExitAll, "loss_cap")]
    [InlineData(HaltLevel.ExitAll, "disaster_loss")]
    [InlineData(HaltLevel.Freeze, "reconciliation_break")]
    [InlineData(HaltLevel.Freeze, "oversold")]
    public async Task The_named_triggers_may_ask_for_exit_all_and_freeze(HaltLevel level, string code)
    {
        await Service().RequestAsync(Account, level, code, "x", CancellationToken.None);

        _store.Rows.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("system:halts")]
    [InlineData("owner:")]
    public async Task An_owner_halt_needs_an_owner_actor(string actor)
    {
        await Should.ThrowAsync<ArgumentException>(() => Service().RequestAsOwnerAsync(Account, HaltLevel.Cancel, "stop", actor, false, CancellationToken.None));

        _store.Rows.ShouldBe(0);
    }

    [Fact]
    public async Task The_owners_halt_carries_the_owner_actor_the_chosen_level_and_the_kill_switch_choice()
    {
        var halt = await Service().RequestAsOwnerAsync(Account, HaltLevel.ExitAll, "gap down", "owner:hem@example.test", brokerKillSwitch: true, CancellationToken.None);

        (halt.Source, halt.Actor, halt.Level, halt.TriggerCode, halt.BrokerKillSwitch).ShouldBe(("owner", "owner:hem@example.test", HaltLevel.ExitAll, "owner_button", true));
    }

    [Fact]
    public async Task An_unreadable_record_reads_as_unreadable_and_blocks_entries_and_a_clear_record_is_the_only_clear_reading()
    {
        var service = Service();
        (await service.ReadAsync(CancellationToken.None)).ShouldBe(HaltReading.Clear);
        HaltPolicy.EntriesBlocked(HaltReading.Clear).ShouldBeFalse();

        _store.Unreadable = true;

        (await service.ReadAsync(CancellationToken.None)).ShouldBe(HaltReading.Unreadable);
        (await service.ActiveAsync(CancellationToken.None)).ShouldBeNull();
        HaltPolicy.EntriesBlocked(await service.ReadAsync(CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_halt_of_another_account_still_reads_as_halted_for_the_entry_gate_which_never_looks_at_accounts()
    {
        await Service().RequestAsync(Guid.NewGuid(), HaltLevel.Entries, HaltTriggers.ProtectionBreach, "x", CancellationToken.None);

        (await Service().ReadAsync(CancellationToken.None)).ShouldBe(HaltReading.Halted);
    }

    [Fact]
    public async Task Only_a_code_that_may_clear_itself_gets_an_automatic_clear_row()
    {
        var service = Service();
        await service.RequestAsync(null, HaltLevel.Entries, HaltTriggers.FeedLoss, "x", CancellationToken.None);
        await service.RequestAsync(Account, HaltLevel.Entries, HaltTriggers.ProtectionBreach, "x", CancellationToken.None);
        var active = (await service.ActiveAsync(CancellationToken.None))!;

        (await _store.AutoClearAsync(active.Single(h => h.TriggerCode == "protection_breach").Id, "protection_breach", "x", _clock.GetUtcNow(), CancellationToken.None)).ShouldBeFalse();
        (await _store.AutoClearAsync(active.Single(h => h.TriggerCode == "feed_loss").Id, "feed_loss", "x", _clock.GetUtcNow(), CancellationToken.None)).ShouldBeTrue();

        (await service.ActiveAsync(CancellationToken.None))!.Select(h => h.TriggerCode).ShouldBe(["protection_breach"]);
    }
}

/// <summary>P12-05 criterion 5: the auto-clearing triggers clear after 60 s (or the reads) of the good state and never otherwise; unavailable is never a good state.</summary>
public sealed class HaltAutoClearTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 4, 0, 0, TimeSpan.Zero));
    private readonly InMemoryHaltStore _store = new();

    private async Task<(HaltAutoClearService Clear, ActiveHalt Halt)> StartAsync(string code, HaltLevel level = HaltLevel.Entries)
    {
        var halt = (await _store.AppendAsync(new HaltDraft(HaltScope.System, null, level, code, "system", "system:halts", "x", _clock.GetUtcNow()), CancellationToken.None)).Halt;
        return (new HaltAutoClearService(_store, _clock), halt);
    }

    [Fact]
    public async Task Feed_loss_clears_after_sixty_seconds_of_continuous_fresh_prices_and_not_a_second_sooner()
    {
        var (clear, halt) = await StartAsync(HaltTriggers.FeedLoss);

        (await clear.ObserveAsync(halt, true, CancellationToken.None)).ShouldBeFalse();
        _clock.Advance(TimeSpan.FromSeconds(59));
        (await clear.ObserveAsync(halt, true, CancellationToken.None)).ShouldBeFalse();
        _clock.Advance(TimeSpan.FromSeconds(1));
        (await clear.ObserveAsync(halt, true, CancellationToken.None)).ShouldBeTrue();

        (await _store.ActiveAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_or_unavailable_reading_restarts_the_quiet_period_and_unavailable_never_clears()
    {
        var (clear, halt) = await StartAsync(HaltTriggers.ProtectionUnknown);
        await clear.ObserveAsync(halt, true, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(50));

        await clear.ObserveAsync(halt, null, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(30));
        (await clear.ObserveAsync(halt, true, CancellationToken.None)).ShouldBeFalse("the count started again at this reading");
        _clock.Advance(TimeSpan.FromSeconds(59));
        (await clear.ObserveAsync(halt, true, CancellationToken.None)).ShouldBeFalse();
        await clear.ObserveAsync(halt, false, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(600));
        (await clear.ObserveAsync(halt, null, CancellationToken.None)).ShouldBeFalse();

        (await _store.ActiveAsync(CancellationToken.None)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Clock_skew_needs_five_consecutive_good_reads_and_broker_outage_three()
    {
        var (skew, skewHalt) = await StartAsync(HaltTriggers.ClockSkew);
        var (outage, outageHalt) = await StartAsync(HaltTriggers.BrokerOutage);
        for (var i = 0; i < 4; i++)
        {
            (await skew.ObserveAsync(skewHalt, true, CancellationToken.None)).ShouldBeFalse();
        }

        (await skew.ObserveAsync(skewHalt, true, CancellationToken.None)).ShouldBeTrue();
        await outage.ObserveAsync(outageHalt, true, CancellationToken.None);
        await outage.ObserveAsync(outageHalt, false, CancellationToken.None);
        await outage.ObserveAsync(outageHalt, true, CancellationToken.None);
        await outage.ObserveAsync(outageHalt, true, CancellationToken.None);
        (await outage.ObserveAsync(outageHalt, true, CancellationToken.None)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("owner_button")]
    [InlineData("daily_loss")]
    [InlineData("drawdown")]
    [InlineData("runaway_rate")]
    [InlineData("exchange_halt")]
    [InlineData("reconciliation_break")]
    [InlineData("protection_breach")]
    public async Task An_owner_only_trigger_is_never_cleared_here_whatever_is_observed(string code)
    {
        var (clear, halt) = await StartAsync(code);
        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(5));
            (await clear.ObserveAsync(halt, true, CancellationToken.None)).ShouldBeFalse();
        }

        (await _store.ActiveAsync(CancellationToken.None)).Count.ShouldBe(1);
        _store.AutoClears.ShouldBeEmpty();
    }
}
