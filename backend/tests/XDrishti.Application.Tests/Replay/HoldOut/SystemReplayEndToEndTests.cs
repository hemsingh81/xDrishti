#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Planning;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.HoldOut;
using XDrishti.Application.Tests.Lab;
using XDrishti.Application.Tests.Research;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Replay.HoldOut;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay.HoldOut;

/// <summary>
/// SR-03 end to end (ADR 0284): the real facts, driver and session runner over an invented market with bars inside and beyond the block, with the registration, the seal, the claim and <see cref="HoldOutSystemRunner.RunPendingAsync"/> in the loop. One result row, figures checked against an independent
/// run of the same arms, no read past the close of the block end, a second pass storing nothing, the stored settings holding against a changed live setting (AC 3), the stored result read back through the view, and the failure paths (a transient failure retried and bounded, any other failure stored <c>run_failed</c>).
/// Block 3 is 2026-09-07 to 2026-12-06 (a 3-month block after block 2 ended 2026-09-06); the market has sessions up to 2026-12-11.
/// </summary>
public sealed class SystemReplayEndToEndTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly DateOnly Two = new(2026, 6, 8);
    private static readonly DateOnly TwoEnd = new(2026, 9, 6);
    private static readonly DateOnly Three = new(2026, 9, 7);
    private static readonly DateOnly ThreeEnd = new(2026, 12, 6);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Scenario
    {
        public required ReplayWorld World { get; init; }

        public required MemoryHoldOutStore Blocks { get; init; }

        public required MemoryRegistrationStore Regs { get; init; }

        public required FakeCostSheetStore Sheets { get; init; }

        public required FakeTimeProvider Time { get; init; }

        public required HoldOutRunAttempts Attempts { get; init; }

        public required IServiceScopeFactory Services { get; init; }

        public required IStrategyStore Strategies { get; init; }

        public ITestLedger Ledger { get; } = Substitute.For<ITestLedger>();

        public Guid RegistrationId { get; set; }

        public HoldOutSystemRunner Runner() => new(Services, Regs, Blocks, Ledger, Engine, Attempts, Time, NullLogger<HoldOutSystemRunner>.Instance);

        public ISimulationEngineVersion Engine { get; } = Version("sim-5");

        public HoldOutInputCheck Check(ReplayFacts facts) => new(facts, Strategies, Sheets, new SystemReplayOptions());
    }

    /// <summary>A scope per arm with the real facts and driver over the world, as the host's container gives them.</summary>
    private sealed class Scopes(ReplayWorld world) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(world);

        private sealed class Scope(ReplayWorld world) : IServiceScope, IServiceProvider
        {
            private ReplayFacts? _facts;
            private ReplayDriver? _driver;

            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IReplayFacts))
                {
                    return _facts ??= world.Facts();
                }

                if (serviceType == typeof(ReplayDriver))
                {
                    var facts = (ReplayFacts)GetService(typeof(IReplayFacts))!;
                    return _driver ??= new ReplayDriver(facts, new ReplaySessionRunner(facts, new BarCandidates(facts), new TicketReplayer(world.Strategies!, world.Baskets!, new FakeSimulation(world)), null));
                }

                return null;
            }

            public void Dispose()
            {
            }
        }
    }

    private static ISimulationEngineVersion Version(string text)
    {
        var engine = Substitute.For<ISimulationEngineVersion>();
        engine.EngineVersion.Returns(text);
        return engine;
    }

    private sealed class TestDbException(string message) : System.Data.Common.DbException(message);

    private static async Task<Scenario> RegisteredAsync(Action<ReplayWorld>? tune = null)
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB");
        // a saw-tooth: runs of rising sessions (candidates) that end in a drop, so trades reach the 4 % target or the 2 % stop inside the block
        world.Series("AAA", d => 100 + (2.5 * (d.DayNumber % 5)));
        world.Series("BBB", d => 100 + (2.5 * ((d.DayNumber + 2) % 5)));
        world.Snapshots.Clear();
        world.Snapshots.Add((Three.AddDays(-3), ReplayWorld.Stats(Three.AddDays(-3))));
        tune?.Invoke(world);

        var blocks = new MemoryHoldOutStore();
        blocks.Blocks.Add(new HoldOutBlock(Two, new DateOnly(2025, 1, 1), TwoEnd, 3, Hash, DateTimeOffset.UnixEpoch));
        var regs = new MemoryRegistrationStore();
        var sheets = new FakeCostSheetStore();
        sheets.With(CostSheet.Create(PaperFixture.SheetId, "flat", "test", "checked 2026-04", PaperFixture.FlatSheet.Schedules).Value);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));
        var engine = Version("sim-5");

        var facts = world.Facts();
        var autoScope = Substitute.For<IAutoScopeStore>();
        var profiles = Substitute.For<IRiskProfileStore>();
        profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<RiskProfile>>([]));
        var register = new RegisterHoldOutHandler(blocks, regs, facts, facts, sheets, autoScope, profiles, engine, new SystemReplayOptions(), new HoldOutOptions(BlockMonths: 3), time);
        var made = await register.HandleAsync(new RegisterHoldOutCommand([ReplayWorld.Basket], null, null, null, PaperFixture.SheetId, null, "owner", "owner:x"), Ct);
        made.IsSuccess.ShouldBeTrue(made.IsFailure ? made.Error.Message : null);
        made.Value.BlockStart.ShouldBe(Three);

        // block 2 is released, block 3 is stored, and its last session has closed
        blocks.Releases.Add(new StoredRelease(Two, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, false, Three));
        blocks.Blocks.Add(new HoldOutBlock(Three, new DateOnly(2025, 1, 1), ThreeEnd, 3, Hash, DateTimeOffset.UnixEpoch, Two));
        time.SetUtcNow(new DateTimeOffset(2026, 12, 7, 6, 0, 0, TimeSpan.Zero));
        return new Scenario { World = world, Blocks = blocks, Regs = regs, Sheets = sheets, Time = time, Attempts = new HoldOutRunAttempts(), Services = new Scopes(world), Strategies = world.Strategies!, RegistrationId = made.Value.Id };
    }

    private static async Task ClaimAsync(Scenario s, string mode = "owner", string actor = "owner:x")
    {
        var facts = s.World.Facts();
        var handler = new RunHoldOutRegistrationHandler(s.Blocks, s.Regs, new HoldOutEpochs(s.Blocks, new FixedCalendar(), s.Time), s.Check(facts), Substitute.For<ITestLedger>(), new HoldOutOptions(BlockMonths: 3), s.Time);
        var run = await handler.HandleAsync(new RunHoldOutRegistrationCommand(s.RegistrationId, mode, actor), Ct);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : null);
    }

    private static HoldOutResultBody Body(StoredHoldOutResult result) => ReplayJson.Read<HoldOutResultBody>(result.FiguresJson)!;

    /// <summary>The same arms run again by the real driver over the same block, for the figures a hand check wants.</summary>
    private static async Task<(int Trades, decimal Equity, decimal? Drawdown, double Cagr)> IndependentAsync(Scenario s, ReplayArm arm)
    {
        var look = s.Blocks.Unlocks.Single(u => u.Kind == "system");
        var reader = new HoldOutBlockReader(HoldOutClaim.From(look, s.RegistrationId).Value, ThreeEnd);
        var facts = s.World.Facts();
        facts.UseBlock(reader);
        var driver = new ReplayDriver(facts, new ReplaySessionRunner(facts, new BarCandidates(facts), new TicketReplayer(s.World.Strategies!, s.World.Baskets!, new FakeSimulation(s.World)), null));
        var config = ReplayJson.Read<HoldOutRegistrationConfig>(s.Regs.Registrations[0].ConfigJson)!;
        var ran = await driver.RunAsync(config.Parameters with { Arm = arm }, Ct);
        ran.IsSuccess.ShouldBeTrue(ran.IsFailure ? ran.Error.Message : null);
        var equity = ran.Value.Final.Equity!.Value;
        var years = (ThreeEnd.DayNumber - Three.DayNumber + 1) / 365.25;
        return (ran.Value.Final.ClosedCount, equity, ran.Value.Final.MaxDrawdownPct, (Math.Pow((double)(equity / config.Parameters.Capital), 1 / years) - 1) * 100);
    }

    [Fact]
    public async Task One_pass_stores_one_result_row_with_the_figures_of_an_independent_run_and_reads_no_bar_past_the_block_end()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);

        var stored = await s.Runner().RunPendingAsync(Ct);

        stored.ShouldBe(1);
        var result = s.Regs.Results.Values.Single();
        (result.Mode, result.Actor, result.UnlockId, result.RunIds.Count).ShouldBe(("owner", "owner:x", s.Blocks.Unlocks.Single(u => u.Kind == "system").RunId, 2));
        var body = Body(result);
        var (trades, equity, drawdown, cagr) = await IndependentAsync(s, ReplayArm.Selection);
        trades.ShouldBeGreaterThan(0, "the invented market trades, so the check is not vacuous");
        body.Figures.Selection!.ClosedTrades.ShouldBe(trades);
        body.Figures.Selection.FinalEquity.ShouldBe(equity);
        body.Figures.Selection.MaxDrawdownPct.ShouldBe(drawdown);
        body.Figures.Selection.CagrPct.ShouldNotBeNull().ShouldBe(cagr, 1e-9);
        body.Figures.Selection.Capital.ShouldBe(1_000_000m);
        body.Figures.Baseline.ShouldNotBeNull();
        body.Figures.SnapshotKeys.ShouldNotBeNull().ShouldNotBeEmpty();
        body.Figures.Failure.ShouldBeNull();
        var expected = SystemReplayVerdict.Evaluate(SystemReplayPassRule.Default, new SystemReplayFigures(trades, body.Figures.Selection.NetBasis, cagr, drawdown, body.Figures.Comparison?.AnswerCode));
        (result.Verdict, body.Verdict.Status, body.Verdict.Reason).ShouldBe((expected.Status, expected.Status, expected.Reason));

        // nothing past the close of the block end was read, and the market does have bars beyond it
        var closeOfEnd = new DateTimeOffset(ThreeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5));
        s.World.Close["AAA"].Keys.Max().ShouldBeGreaterThan(ThreeEnd);
        s.World.MinuteReadEnds.ShouldNotBeEmpty();
        s.World.DayReadEnds.ShouldNotBeEmpty();
        s.World.MinuteReadEnds.Where(e => e > closeOfEnd).ShouldBeEmpty();
        s.World.DayReadEnds.Where(e => e > ThreeEnd.AddDays(1)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_pass_stores_nothing_and_the_stored_result_reads_back_through_the_view_as_run()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        await s.Runner().RunPendingAsync(Ct);

        var again = await s.Runner().RunPendingAsync(Ct);

        again.ShouldBe(0);
        s.Regs.Results.Count.ShouldBe(1);
        var facts = s.World.Facts();
        var views = new HoldOutRegistrationViews(s.Regs, s.Blocks, s.Check(facts));
        var view = await new GetHoldOutRegistrationHandler(s.Regs, views).HandleAsync(new GetHoldOutRegistrationQuery(s.RegistrationId), Ct);
        view.IsSuccess.ShouldBeTrue(view.IsFailure ? view.Error.Message : null);
        view.Value.State.ShouldBe("run");
        view.Value.Result.ShouldNotBeNull();
        (view.Value.Result.Verdict, view.Value.Result.Figures.Selection!.ClosedTrades).ShouldBe((s.Regs.Results.Values.Single().Verdict, Body(s.Regs.Results.Values.Single()).Figures.Selection!.ClosedTrades));
        view.Value.Hindsight.ShouldContain("Unseen by the system");
    }

    [Fact]
    public async Task A_changed_live_setting_after_registration_gives_the_same_figures_because_the_run_uses_the_stored_snapshot()
    {
        var control = await RegisteredAsync();
        await ClaimAsync(control);
        await control.Runner().RunPendingAsync(Ct);

        // the live limits would stop every swing position (MaxSwing 0) if the run read them
        var changed = await RegisteredAsync();
        changed.World.Fill = changed.World.Fill with { MaxSwing = 0 };
        await ClaimAsync(changed);
        await changed.Runner().RunPendingAsync(Ct);

        var (a, b) = (Body(control.Regs.Results.Values.Single()).Figures, Body(changed.Regs.Results.Values.Single()).Figures);
        a.Selection!.ClosedTrades.ShouldBeGreaterThan(0);
        (b.Selection!.ClosedTrades, b.Selection.FinalEquity, b.Selection.MaxDrawdownPct, b.Selection.CagrPct).ShouldBe((a.Selection.ClosedTrades, a.Selection.FinalEquity, a.Selection.MaxDrawdownPct, a.Selection.CagrPct));
        (b.Baseline!.ClosedTrades, b.Baseline.FinalEquity).ShouldBe((a.Baseline!.ClosedTrades, a.Baseline.FinalEquity));
    }

    [Fact]
    public async Task A_transient_failure_keeps_the_claim_and_a_later_pass_stores_the_result()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        s.World.ReadFailure = new IOException("disk");

        var first = await s.Runner().RunPendingAsync(Ct);
        var second = await s.Runner().RunPendingAsync(Ct);
        s.World.ReadFailure = null;
        var third = await s.Runner().RunPendingAsync(Ct);

        (first, second, third).ShouldBe((0, 0, 1));
        s.Regs.Results.Values.Single().Verdict.ShouldNotBe("run_failed");
        s.Blocks.Unlocks.Count(u => u.Kind == "system").ShouldBe(1, "the claim was never taken again");
    }

    [Fact]
    public async Task A_database_failure_wrapped_by_the_retrying_strategy_is_still_transient_and_the_claim_stays()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        s.World.ReadFailure = new InvalidOperationException("retry limit exceeded", new TestDbException("connection reset"));

        var first = await s.Runner().RunPendingAsync(Ct);
        s.World.ReadFailure = null;
        var second = await s.Runner().RunPendingAsync(Ct);

        (first, second).ShouldBe((0, 1));
        s.Regs.Results.Values.Single().Verdict.ShouldNotBe("run_failed");
    }

    [Fact]
    public async Task The_runner_appends_the_ledger_rows_before_it_reads_and_a_failing_ledger_means_no_read_and_a_retry()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        s.Ledger.AppendAsync(Arg.Any<LedgerRow>(), Arg.Any<CancellationToken>()).Returns<ValueTask<bool>>(_ => throw new TestDbException("ledger down"));

        var failed = await s.Runner().RunPendingAsync(Ct);

        failed.ShouldBe(0);
        s.World.MinuteReadEnds.ShouldBeEmpty("no read of the block without its ledger rows");
        s.Regs.Results.ShouldBeEmpty();
        s.Ledger.ClearReceivedCalls();
        s.Ledger.AppendAsync(Arg.Any<LedgerRow>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(true));

        var stored = await s.Runner().RunPendingAsync(Ct);

        stored.ShouldBe(1);
        await s.Ledger.Received().AppendAsync(Arg.Is<LedgerRow>(r => r.TrialKind == LedgerRow.SystemReplayKind && r.StrategyId == "S1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_run_freezes_on_the_snapshot_as_of_before_the_block_even_when_the_first_sessions_own_refresh_was_written_that_evening()
    {
        // LC-01 writes a snapshot every evening, as of that day: the one as of 2026-09-07 (the block's first session) is created after the block began
        var s = await RegisteredAsync(w =>
        {
            w.Snapshots.Clear();
            for (var day = Three.AddDays(-6); day <= Three.AddDays(10); day = day.AddDays(1))
            {
                var evening = new DateTimeOffset(day.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromHours(5.5));
                w.Snapshots.Add((day, ReplayWorld.Stats(day) with { CreatedAt = evening }));
            }
        });

        await ClaimAsync(s);
        var stored = await s.Runner().RunPendingAsync(Ct);

        stored.ShouldBe(1);
        var figures = Body(s.Regs.Results.Values.Single()).Figures;
        figures.SnapshotKeys.ShouldBe(["snap20260906"]);
        figures.SnapshotCreatedAt.ShouldBe(new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(5.5)));
    }

    [Fact]
    public async Task A_transient_failure_that_goes_on_is_stored_run_failed_after_the_bounded_tries()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        s.World.ReadFailure = new TimeoutException("slow");

        var stored = new List<int>();
        for (var i = 0; i < HoldOutRunAttempts.Max; i++)
        {
            stored.Add(await s.Runner().RunPendingAsync(Ct));
        }

        stored.ShouldBe([.. Enumerable.Repeat(0, HoldOutRunAttempts.Max - 1), 1]);
        var result = s.Regs.Results.Values.Single();
        (result.Verdict, Body(result).Verdict.Reason.Contains(string.Create(CultureInfo.InvariantCulture, $"{HoldOutRunAttempts.Max} tries"), StringComparison.Ordinal)).ShouldBe(("run_failed", true));
        (await s.Runner().RunPendingAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Any_other_failure_is_stored_run_failed_at_once_and_never_as_inconclusive()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        s.World.ReadFailure = new InvalidOperationException("a defect");

        var stored = await s.Runner().RunPendingAsync(Ct);

        stored.ShouldBe(1);
        var result = s.Regs.Results.Values.Single();
        (result.Verdict, Body(result).Figures.Failure).ShouldBe(("run_failed", "run_failed: InvalidOperationException"));
        (await s.Runner().RunPendingAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_stored_block_that_no_longer_ends_where_registered_reads_nothing_and_is_stored_run_failed_once()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s);
        s.Blocks.Blocks.RemoveAll(b => b.Start == Three);
        s.Blocks.Blocks.Add(new HoldOutBlock(Three, new DateOnly(2025, 1, 1), ThreeEnd.AddMonths(3), 6, Hash, DateTimeOffset.UnixEpoch, Two));

        var stored = await s.Runner().RunPendingAsync(Ct);
        var again = await s.Runner().RunPendingAsync(Ct);

        (stored, again).ShouldBe((1, 0));
        s.Regs.Results.Values.Single().Verdict.ShouldBe("run_failed");
        s.World.MinuteReadEnds.ShouldBeEmpty("no bar of the block was read");
    }

    [Fact]
    public async Task An_auto_claim_is_recorded_as_auto_with_its_actor_on_the_stored_result()
    {
        var s = await RegisteredAsync();
        await ClaimAsync(s, "auto", "system:holdout-replay");

        await s.Runner().RunPendingAsync(Ct);

        (s.Regs.Results.Values.Single().Mode, s.Regs.Results.Values.Single().Actor).ShouldBe(("auto", "system:holdout-replay"));
    }
}
