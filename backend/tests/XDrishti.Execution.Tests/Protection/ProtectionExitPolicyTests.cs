using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>The reduce-only rule behind the sender (P12-04 I3 to I5), and the proof that the default stays closed.</summary>
public sealed class ProtectionExitPolicyTests
{
    private static async Task<(Rig Rig, ProtectionExitPolicy Policy)> HeldAsync(int net = 10)
    {
        var rig = new Rig();
        await rig.FilledAsync(net);
        return (rig, new ProtectionExitPolicy(rig.Source, () => rig.Settings, rig.Clock));
    }

    private static BrokerLeg Exit(int qty = 10, LegKind kind = LegKind.Exit, ExecSide side = ExecSide.Sell) => new(Rig.Cid(kind, 1), kind, side, "1594", qty, 1490m);

    private static async Task<string> ReasonAsync(ProtectionExitPolicy policy, BrokerLeg leg, long intent = 361, Guid? account = null) =>
        (await policy.CheckAsync(account ?? Rig.Account, intent, leg, CancellationToken.None)).Reason;

    [Fact]
    public async Task A_reduce_only_sell_up_to_the_position_is_allowed()
    {
        var (_, policy) = await HeldAsync();

        (await ReasonAsync(policy, Exit(10))).ShouldBe("open");
        (await ReasonAsync(policy, Exit(10, LegKind.Flatten))).ShouldBe("open");
        (await ReasonAsync(policy, Rig.Stop(10))).ShouldBe("open");
    }

    [Theory]
    [InlineData(11, "exceeds_position")]
    [InlineData(0, "quantity")]
    [InlineData(-1, "quantity")]
    public async Task An_exit_above_the_position_or_without_a_quantity_is_refused(int qty, string reason)
    {
        var (_, policy) = await HeldAsync();

        (await ReasonAsync(policy, Exit(qty))).ShouldBe(reason);
    }

    [Fact]
    public async Task Nothing_is_allowed_for_a_buy_a_target_an_entry_a_flat_book_an_unknown_intent_or_another_account_or_security()
    {
        var (rig, policy) = await HeldAsync();

        (await ReasonAsync(policy, Exit(side: ExecSide.Buy))).ShouldBe("reduce_only_side");
        (await ReasonAsync(policy, Exit(kind: LegKind.Target))).ShouldBe("target_not_resting");
        (await ReasonAsync(policy, Rig.Entry())).ShouldBe("not_a_reducing_leg");
        (await ReasonAsync(policy, Exit(), intent: 999)).ShouldBe("no_position");
        (await ReasonAsync(policy, Exit(), account: Guid.NewGuid())).ShouldBe("no_position");
        (await ReasonAsync(policy, Exit() with { SecurityId = "5" })).ShouldBe("no_position");
        rig.Source.NetOverride = 0;
        (await ReasonAsync(policy, Exit(1))).ShouldBe("no_position", "no exit is sent at q = 0");
        rig.Source.NetOverride = -4;
        (await ReasonAsync(policy, Exit(1))).ShouldBe("oversold_nothing_sent");
    }

    [Fact]
    public async Task Exits_already_resting_reduce_what_may_still_be_sold()
    {
        var (rig, policy) = await HeldAsync();
        await rig.Broker.PlaceAsync(Exit(4) with { Price = 1600m }, CancellationToken.None);

        (await ReasonAsync(policy, Exit(7, LegKind.Flatten) with { ClientOrderId = Rig.Cid(LegKind.Flatten, 1) })).ShouldBe("exceeds_position");
        (await ReasonAsync(policy, Exit(6, LegKind.Flatten) with { ClientOrderId = Rig.Cid(LegKind.Flatten, 1) })).ShouldBe("open");
    }

    [Fact]
    public async Task A_stop_covers_only_what_no_live_stop_covers_never_sits_below_the_intents_and_has_its_limit_under_the_trigger()
    {
        var (rig, policy) = await HeldAsync();
        await rig.StopAsync(6);

        (await ReasonAsync(policy, Rig.Stop(5, attempt: 2))).ShouldBe("stop_exceeds_uncovered");
        (await ReasonAsync(policy, Rig.Stop(4, attempt: 2))).ShouldBe("open");
        (await ReasonAsync(policy, Rig.Stop(4, 1480m, 2))).ShouldBe("stop_looser_than_intent");
        (await ReasonAsync(policy, Rig.Stop(4, attempt: 2) with { Price = 1490m })).ShouldBe("stop_limit_above_trigger");
        (await ReasonAsync(policy, Rig.Stop(4, attempt: 2) with { Trigger = null })).ShouldBe("stop_limit_above_trigger");
    }

    [Fact]
    public async Task No_snapshot_a_stale_book_or_no_settings_is_a_refusal()
    {
        var (rig, policy) = await HeldAsync();
        rig.Source.BookAge = TimeSpan.FromSeconds(31);
        (await ReasonAsync(policy, Exit())).ShouldBe("book_stale");
        rig.Source.BookAge = TimeSpan.Zero;
        rig.Source.Unknown = true;
        (await ReasonAsync(policy, Exit())).ShouldBe("protection_unknown");
        rig.Source.Unknown = false;
        (await ReasonAsync(new ProtectionExitPolicy(rig.Source, () => null, rig.Clock), Exit())).ShouldBe("protection_settings_unavailable");
    }

    [Fact]
    public async Task Under_freeze_the_quantity_is_also_bounded_by_the_brokers_net_from_a_read_at_most_15_seconds_old()
    {
        var (rig, policy) = await HeldAsync();
        rig.Source.Halt = HaltLevel.Freeze;

        (await ReasonAsync(policy, Exit(10))).ShouldBe("freeze_needs_position_read");
        rig.Source.BrokerNet = 10;
        rig.Source.BrokerPositionAt = rig.Clock.GetUtcNow();
        (await ReasonAsync(policy, Exit(10))).ShouldBe("open");
        rig.Source.BrokerNet = 9;
        (await ReasonAsync(policy, Exit(9))).ShouldBe("freeze_needs_position_read", "the broker holds less than we think");
        rig.Source.BrokerNet = 10;
        rig.Clock.Advance(TimeSpan.FromSeconds(16));
        rig.Source.BookAge = TimeSpan.Zero;
        (await ReasonAsync(policy, Exit(10))).ShouldBe("freeze_needs_position_read");
    }

    [Fact]
    public async Task The_closed_default_still_refuses_every_leg_and_the_closed_source_answers_unknown()
    {
        var closedPolicy = new ClosedExitSendPolicyProbe();
        (await closedPolicy.AskAsync(Exit())).Open.ShouldBeFalse();

        var rig = new Rig();
        var sender = new LegSender(rig.Broker, new FixedMode(new ModeAnswer(ExecMode.SemiAuto, true)), new ClearHalts(), new ExitOnlyPreSend(closedPolicy.Policy), new NullJournal(), new ExecRateLimiter(rig.Clock, 5, 100, 1000), ExecEnvironment.Sandbox, rig.Clock, TimeSpan.FromSeconds(5));
        await rig.FilledAsync();
        var result = await sender.SendAsync(new SendRequest(Rig.Account, 361, Exit(), rig.Clock.GetUtcNow()), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Refused, "no_exit_policy"));
    }

    [Fact]
    public void The_protection_gate_ignores_the_mode_but_needs_the_switch_the_environment_and_an_answer()
    {
        foreach (var mode in Enum.GetValues<ExecMode>())
        {
            ProtectionGate.MayProtect(new ModeAnswer(mode, true), ExecEnvironment.Sandbox).Open.ShouldBeTrue();
        }

        ProtectionGate.MayProtect(new ModeAnswer(ExecMode.AutoLive, false), ExecEnvironment.Sandbox).Reason.ShouldBe("execution_disabled");
        ProtectionGate.MayProtect(null, ExecEnvironment.Sandbox).Reason.ShouldBe("no_mode_answer");
        ProtectionGate.MayProtect(new ModeAnswer(ExecMode.AutoLive, true), ExecEnvironment.None).Reason.ShouldBe("environment_none");
        AccountGate.MayAct(new ModeAnswer(ExecMode.Paper, true), ExecEnvironment.Sandbox).Open.ShouldBeFalse("an entry still needs an executing mode");
    }

    // The internal closed default is reached through the host composition in production; here it is read off the port's contract by a stand-in with the same answer.
    private sealed class ClosedExitSendPolicyProbe
    {
        public IExitSendPolicy Policy { get; } = (IExitSendPolicy)Activator.CreateInstance(typeof(IExitSendPolicy).Assembly.GetType("XDrishti.Execution.Application.Ports.ClosedExitSendPolicy")!, nonPublic: true)!;

        public Task<GateDecision> AskAsync(BrokerLeg leg) => Policy.CheckAsync(Rig.Account, 361, leg, CancellationToken.None);
    }
}
