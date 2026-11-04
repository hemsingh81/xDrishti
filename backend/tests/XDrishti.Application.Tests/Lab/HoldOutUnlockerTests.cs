using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-06 AC9 to AC11: one look per version, the first look at a block is fresh and every later one is re-used and cannot pass, the owner and auto modes, and the owner's unlock handler.</summary>
public sealed class HoldOutUnlockerTests
{
    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();
    private readonly ILabResultStore _results = Substitute.For<ILabResultStore>();
    private readonly IPlanFreezeStore _freezes = Substitute.For<IPlanFreezeStore>();
    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();
    private readonly IAccountUnitOfWork _unitOfWork = Substitute.For<IAccountUnitOfWork>();
    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly MemoryHoldOutStore _store = new();
    private readonly FakePass _pass = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

    public HoldOutUnlockerTests()
    {
        var first = new DateTimeOffset(2024, 10, 1, 9, 15, 0, TimeSpan.FromHours(5.5));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(2, 1000, first, new DateTimeOffset(2026, 10, 30, 15, 29, 0, TimeSpan.FromHours(5.5)), 0, 0));
        _freezes.FirstAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new string('f', 64));
    }

    private HoldOutUnlocker Unlocker(HoldOutOptions? options = null)
    {
        var o = options ?? new HoldOutOptions();
        return new HoldOutUnlocker(_runs, _results, _store, _freezes, _pass, new LabHoldOutGuard(_store, _bars, o, _time), o, _time, _events, _unitOfWork, NullLogger<HoldOutUnlocker>.Instance);
    }

    private LabRun Run(string strategy = "FAM", int version = 1, DateOnly? to = null, string method = "robust", LabRunState state = LabRunState.Succeeded)
    {
        var run = new LabRun(Guid.NewGuid(), new string('k', 64), strategy, version, Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
            new DateOnly(2024, 10, 1), to ?? new DateOnly(2026, 4, 30), method, new string('c', 64), "sim-5", state, null, 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, null, null);
        _runs.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
        _results.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(Verdict());
        return run;
    }

    internal static LabVerdictView Verdict(string outcome = "PASS", double? expectancy = 0.20, bool gatesPass = true, bool? enough = true, string? tradable = "pass", double? dsr = 0.97) => new(
        outcome, 3, 1.5,
        new EvidenceView(120, expectancy, 1.5, 8, 6, 3, 0, "walk_forward", enough is null ? null : new XDrishti.Application.Confidence.DependenceView("ReportOnly", 120, 90, 1.3, "cluster", 100, 0.1, 80, 90, 1, enough, "n", "s")),
        [new GateView("oos_trades", 30, 45, 120, gatesPass, "ok")],
        [],
        [new CellView("c", "15m", "BUY", "FIXED_RR:rr=2", "FIXED_RR:rr=2", null, true, "stable", 8, 8, 120, expectancy, expectancy)],
        [],
        [],
        DeflatedSharpe: dsr is null ? null : new DeflatedSharpeView("ReportOnly", 5, 5, 0.1, 0.2, 0.3, 100, 0, 3, dsr, 0.95, "nEff", "s", "n"),
        CostStress: tradable is null ? null : CostStressWith(tradable));

    private static CostStressView CostStressWith(string tradable) => new("ReportOnly", false, "s", new string('h', 64), true, "sheet", [], tradable, null, false, "f", "l");

    [Fact]
    public async Task The_first_look_runs_once_is_fresh_and_a_pass_when_the_rule_is_met_and_it_stores_the_result_with_the_setting_hash()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var run = Run();

        var result = await Unlocker().UnlockAsync(run.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var v = result.Value;
        (v.Status, v.Passed, v.State, v.ReusedWindow, v.Mode, v.Actor, v.Trades).ShouldBe(("pass", true, "used", false, "owner", "owner:x", 45));
        v.Ratio.ShouldNotBeNull().ShouldBe(0.6, 1e-9);
        v.SettingHash.ShouldBe(new HoldOutOptions().SnapshotHash());
        v.PlanHash.ShouldBe(new string('f', 64));
        _store.Unlocks.Count.ShouldBe(1);
        _store.Blocks.Single().Start.ShouldBe(new DateOnly(2026, 5, 1));
        _pass.Calls.ShouldBe(1);
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.LabHoldOutUnlocked && e.Actor == "owner:x" && e.Detail!.Contains("mode=owner", StringComparison.Ordinal) && e.Detail.Contains("v=1", StringComparison.Ordinal)));
        await _unitOfWork.Received(1).TrySaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_second_look_by_the_same_version_is_refused_and_the_stored_result_is_unchanged()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var run = Run();
        await Unlocker().UnlockAsync(run.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        var stored = _store.Unlocks.Single().Result;
        _pass.Figures = new HoldOutFigures(45, -0.5, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));

        var again = await Unlocker().UnlockAsync(run.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        again.Error.Code.ShouldBe(LabErrors.HoldOutUsedCode);
        again.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _store.Unlocks.Single().Result.ShouldBe(stored);
        _events.ReceivedWithAnyArgs(1).Record(default!);
        _pass.Calls.ShouldBe(1, "the plan is not run a second time");
    }

    [Fact]
    public async Task Another_version_may_look_but_the_window_is_re_used_and_the_pass_flag_is_false_whatever_the_numbers()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        await Unlocker().UnlockAsync(Run(version: 1).Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        _pass.Figures = new HoldOutFigures(60, 0.40, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));

        var second = await Unlocker().UnlockAsync(Run(version: 2).Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        second.IsSuccess.ShouldBeTrue();
        (second.Value.State, second.Value.ReusedWindow, second.Value.Passed, second.Value.Status).ShouldBe(("reused", true, false, "pass"));
        second.Value.HoldOutExpectancyR.ShouldBe(0.40, "the numbers are shown");
        second.Value.Notes.ShouldNotBeEmpty();
        _store.Unlocks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_versions_looking_at_once_cannot_both_be_fresh_the_loser_is_stored_as_re_used()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        await Unlocker().UnlockAsync(Run(version: 1).Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        _store.HideLooksFromAny = true;

        var second = await Unlocker().UnlockAsync(Run(version: 2).Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        second.IsSuccess.ShouldBeTrue();
        (second.Value.ReusedWindow, second.Value.Passed).ShouldBe((true, false));
        _store.Unlocks.Count(u => !u.ReusedWindow).ShouldBe(1);
    }

    [Fact]
    public async Task A_run_whose_window_reached_the_hold_out_is_never_a_pass()
    {
        _pass.Figures = new HoldOutFigures(45, 0.20, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var run = Run(to: new DateOnly(2026, 10, 30));

        var result = await Unlocker().UnlockAsync(run.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        (result.Value.RunSawHoldOut, result.Value.Passed).ShouldBe((true, false));
        result.Value.Reason.ShouldContain("window reached the hold-out");
    }

    [Fact]
    public async Task Few_trades_are_inconclusive_and_a_run_that_is_not_a_succeeded_full_test_has_nothing_to_unlock()
    {
        _pass.Figures = new HoldOutFigures(22, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));

        (await Unlocker().UnlockAsync(Run().Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct)).Value.Status.ShouldBe("inconclusive");
        (await Unlocker().UnlockAsync(Run(strategy: "Q", method: "quick").Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.NoReportCode);
        (await Unlocker().UnlockAsync(Run(strategy: "R", state: LabRunState.Failed).Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct)).Error.Code.ShouldBe(LabErrors.NoReportCode);
        (await Unlocker().UnlockAsync(Guid.NewGuid(), HoldOutUnlocker.OwnerMode, "owner:x", Ct)).Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task A_history_too_short_for_any_hold_out_is_refused_and_nothing_is_run_or_stored()
    {
        var first = new DateTimeOffset(2026, 9, 1, 9, 15, 0, TimeSpan.FromHours(5.5));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(2, 1000, first, first.AddDays(40), 0, 0));

        var result = await Unlocker().UnlockAsync(Run().Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _store.Unlocks.ShouldBeEmpty();
        _pass.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_failure_of_the_pass_stores_no_unlock()
    {
        _pass.Failure = LabErrors.HoldOutUnavailable("no sheet");

        var result = await Unlocker().UnlockAsync(Run().Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        result.IsFailure.ShouldBeTrue();
        _store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_auto_option_off_never_runs_and_on_it_runs_once_recorded_as_auto_when_every_development_row_passes()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var run = Run();

        (await Unlocker(new HoldOutOptions(AutoUnlock: false)).TryAutoAsync(run.Id, Verdict(), Ct)).ShouldBeNull();
        _store.Unlocks.ShouldBeEmpty();
        var done = await Unlocker(new HoldOutOptions(AutoUnlock: true)).TryAutoAsync(run.Id, Verdict(), Ct);
        var second = await Unlocker(new HoldOutOptions(AutoUnlock: true)).TryAutoAsync(run.Id, Verdict(), Ct);

        done.ShouldNotBeNull().Mode.ShouldBe("auto");
        done.Actor.ShouldBe("system:auto-unlock");
        done.SettingHash.ShouldBe(new HoldOutOptions(AutoUnlock: true).SnapshotHash(), "the snapshot with the setting ON is recorded");
        second.ShouldBeNull("it ran once");
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.LabHoldOutUnlocked && e.Actor == "system:auto-unlock" && e.Detail!.Contains("mode=auto", StringComparison.Ordinal)));
        _store.Unlocks.Single().Mode.ShouldBe("auto");
    }

    [Fact]
    public async Task The_auto_option_does_not_run_while_any_development_row_is_not_a_pass_or_is_unavailable()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var run = Run();
        var on = Unlocker(new HoldOutOptions(AutoUnlock: true));

        (await on.TryAutoAsync(run.Id, Verdict(outcome: "MARGINAL"), Ct)).ShouldBeNull();
        (await on.TryAutoAsync(run.Id, Verdict(gatesPass: false), Ct)).ShouldBeNull();
        (await on.TryAutoAsync(run.Id, Verdict(enough: null), Ct)).ShouldBeNull("effective n unavailable");
        (await on.TryAutoAsync(run.Id, Verdict(tradable: "unavailable"), Ct)).ShouldBeNull("cost stress unavailable");
        (await on.TryAutoAsync(run.Id, Verdict(tradable: null), Ct)).ShouldBeNull("no cost stress block");
        (await on.TryAutoAsync(run.Id, Verdict(dsr: null), Ct)).ShouldBeNull("no deflated Sharpe");
        (await on.TryAutoAsync(run.Id, Verdict(dsr: 0.5), Ct)).ShouldBeNull();
        _store.Unlocks.ShouldBeEmpty();
        _pass.Calls.ShouldBe(0);
    }

    [Fact]
    public void The_rows_name_each_development_check_and_unavailable_is_never_a_pass()
    {
        var before = new DateOnly(2026, 4, 30);
        var start = new DateOnly(2026, 5, 1);
        var rows = HoldOutAutoGate.Rows(Verdict(enough: null), before, start);

        rows.Select(r => r.Name).ShouldBe(["verdict", "gates", "effective_n", "cost_stress", "deflated_sharpe", "window_clear"]);
        rows.Single(r => r.Name == "effective_n").Status.ShouldBe("unavailable");
        HoldOutAutoGate.AllPass(Verdict(enough: null), before, start).ShouldBeFalse();
        HoldOutAutoGate.AllPass(Verdict(), before, start).ShouldBeTrue();
        HoldOutAutoGate.AllPass(Verdict(), start, start).ShouldBeFalse("the window reaches the block");
        HoldOutAutoGate.AllPass(Verdict(), null, start).ShouldBeFalse("no window is unavailable");
        HoldOutAutoGate.AllPass(Verdict(), before, null).ShouldBeFalse("no block is unavailable");
    }

    [Fact]
    public async Task The_auto_option_does_not_unlock_a_run_whose_explicit_window_reached_the_block_and_keeps_the_fresh_look_for_a_later_version()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var reaching = Run(version: 1, to: new DateOnly(2026, 6, 30));
        var on = Unlocker(new HoldOutOptions(AutoUnlock: true));

        (await on.TryAutoAsync(reaching.Id, Verdict(), Ct)).ShouldBeNull();

        _store.Unlocks.ShouldBeEmpty();
        _pass.Calls.ShouldBe(0);
        var later = await Unlocker().UnlockAsync(Run(version: 2).Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        later.Value.ReusedWindow.ShouldBeFalse("the fresh slot was not burned");
        // the owner path is unchanged: a reaching run may still be unlocked by the owner, and is stored but cannot count
        var owner = await Unlocker().UnlockAsync(reaching.Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);
        owner.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_owner_handler_refuses_an_empty_actor_and_otherwise_runs_the_look_as_owner()
    {
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        var run = Run();
        var handler = new UnlockHoldOutHandler(Unlocker());

        (await handler.HandleAsync(new UnlockHoldOutCommand(run.Id, " "), Ct)).Error.Kind.ShouldBe(ErrorKind.Forbidden);
        (await handler.HandleAsync(new UnlockHoldOutCommand(run.Id, "owner:x"), Ct)).Value.Mode.ShouldBe("owner");
    }

    [Fact]
    public async Task The_hold_out_view_shows_the_proposal_before_a_block_exists_and_numbers_only_in_a_look_that_was_taken()
    {
        var handler = new GetHoldOutHandler(_store, new LabHoldOutGuard(_store, _bars, new HoldOutOptions(), _time), new HoldOutOptions());

        var before = (await handler.HandleAsync(new GetHoldOutQuery(), Ct)).Value;

        before.Block.ShouldBeNull();
        before.Proposed.ShouldNotBeNull().Start.ShouldBe(new DateOnly(2026, 5, 1));
        before.Unlocks.ShouldBeEmpty();
        before.Enforce.ShouldBeFalse();
        _pass.Figures = new HoldOutFigures(45, 0.12, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30));
        await Unlocker().UnlockAsync(Run().Id, HoldOutUnlocker.OwnerMode, "owner:x", Ct);

        var after = (await handler.HandleAsync(new GetHoldOutQuery(), Ct)).Value;

        after.Block.ShouldNotBeNull().Start.ShouldBe(new DateOnly(2026, 5, 1));
        after.Proposed.ShouldBeNull();
        after.Unlocks.Single().HoldOutExpectancyR.ShouldBe(0.12);
    }

    private sealed class FakePass : IHoldOutPass
    {
        public HoldOutFigures? Figures { get; set; }

        public Error? Failure { get; set; }

        public int Calls { get; private set; }

        public ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(Failure is { } e ? Result.Failure<HoldOutFigures>(e) : Result.Success(Figures!));
        }
    }
}

/// <summary>An in-memory hold-out store with the same uniqueness as the tables.</summary>
internal sealed class MemoryHoldOutStore : IHoldOutStore
{
    public List<HoldOutBlock> Blocks { get; } = [];

    public List<StoredUnlock> Unlocks { get; } = [];

    public ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Blocks.FirstOrDefault());

    public ValueTask<bool> InsertBlockAsync(HoldOutBlock block, CancellationToken cancellationToken)
    {
        if (Blocks.Any(b => b.Start == block.Start))
        {
            return ValueTask.FromResult(false);
        }

        Blocks.Add(block);
        return ValueTask.FromResult(true);
    }

    public ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Unlocks.FirstOrDefault(u => u.StrategyId == strategyId && u.Version == version && u.Kind == kind));

    /// <summary>Set to make the read before the insert miss a look that lands at the same moment (the race the partial unique index closes).</summary>
    public bool HideLooksFromAny { get; set; }

    public ValueTask<bool> AnyUnlockAsync(DateOnly blockStart, CancellationToken cancellationToken) => ValueTask.FromResult(!HideLooksFromAny && Unlocks.Any(u => u.BlockStart == blockStart));

    public ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredUnlock>>([.. Unlocks.AsEnumerable().Reverse().Take(limit)]);

    public ValueTask<bool> InsertUnlockAsync(StoredUnlock unlock, CancellationToken cancellationToken)
    {
        if (Unlocks.Any(u => u.StrategyId == unlock.StrategyId && u.Version == unlock.Version && u.Kind == unlock.Kind)
            || (!unlock.ReusedWindow && Unlocks.Any(u => u.BlockStart == unlock.BlockStart && !u.ReusedWindow)))
        {
            return ValueTask.FromResult(false);
        }

        Unlocks.Add(unlock);
        return ValueTask.FromResult(true);
    }
}
