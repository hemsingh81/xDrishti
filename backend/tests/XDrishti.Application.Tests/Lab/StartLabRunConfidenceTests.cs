#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-06 on the post of a run (AC1, 4, 8): the registration check under both settings, the extra exit looks, the plan freeze with its re-plan row, and the cut of the window by the hold-out.</summary>
public sealed class StartLabRunConfidenceTests
{
    private static readonly string Hash = new('a', 64);

    private static readonly DateTimeOffset FirstBar = new(2026, 1, 5, 9, 15, 0, TimeSpan.FromHours(5.5));

    private static readonly DateTimeOffset LastBar = new(2026, 10, 30, 15, 29, 0, TimeSpan.FromHours(5.5));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();
    private readonly ITestLedger _ledger = Substitute.For<ITestLedger>();
    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>> _resolver = Substitute.For<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>>();
    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();
    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly ISimulationEngine _engine = Substitute.For<ISimulationEngine>();
    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();
    private readonly IRegistrationStore _registrations = Substitute.For<IRegistrationStore>();
    private readonly IPlanFreezeStore _freezes = Substitute.For<IPlanFreezeStore>();
    private readonly IHoldOutStore _holdOuts = TestHoldOutStores.NoReleases();
    private readonly IStrategyChecker _checker = Substitute.For<IStrategyChecker>();
    private readonly List<LedgerRow> _appended = [];
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero));

    public StartLabRunConfidenceTests()
    {
        _strategies.GetVersionAsync("LAB_TEST", Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 2, 1, Hash, "t", "{}", null, null, DateTimeOffset.UnixEpoch));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(214, 40_000_000, FirstBar, LastBar, 0, 0));
        _engine.EngineVersion.Returns("sim-5");
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _resolver.HandleAsync(Arg.Any<ResolveInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Matched(3)));
        _checker.Check(Arg.Any<string>()).Returns(new StrategyCheck(new StrategyDescription("LAB_TEST", "intraday", Hash, "{}", [Timeframe.M15], [Side.Buy], [new KeyValuePair<string, double>("period", 10)]), 1, []));
        _runs.InsertOrGetAsync(Arg.Any<NewLabRun>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var n = ci.Arg<NewLabRun>();
            return new InsertedLabRun(new LabRun(n.Id, n.Key, n.StrategyId, n.Version, n.StrategyHash, n.Universe, n.From, n.To, n.Method, n.ConfigHash, n.EngineVersion, LabRunState.Queued, null, 0, 0, [], null, [], [], n.Labels, null, false, n.CreatedAt, null, null), true);
        });
        _ledger.AppendAsync(Arg.Any<LedgerRow>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _appended.Add(ci.Arg<LedgerRow>());
            return true;
        });
        _freezes.FreezeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new PlanFreezeOutcome(true, 0));
    }

    private static ResolveResult Matched(int count) =>
        new([.. Enumerable.Range(1, count).Select(i => new ResolvedInstrument("S" + i, new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "S" + i, "Name " + i, "EQUITY", null, false, true))], [], count, 0);

    private StartLabRunHandler Handler(bool enforceRegistration = false, HoldOutOptions? holdOut = null)
    {
        var pre = new LabPreregistration(_registrations, _freezes, _ledger, _sheets, _checker, LabSettings.Default, new RegistrationOptions(enforceRegistration));
        var guard = new LabHoldOutGuard(_holdOuts, _bars, holdOut ?? new HoldOutOptions(), _time);
        return new StartLabRunHandler(_runs, _ledger, _strategies, _resolver, _baskets, _bars, _engine, _sheets, LabSettings.Default, _time, NullLogger<StartLabRunHandler>.Instance, pre, guard);
    }

    private static StartLabRunCommand Full(string method = "full", DateOnly? from = null, DateOnly? to = null) => new("LAB_TEST", null, null, ["S1"], method, from, to);

    private static StoredRegistration Registered() => new("LAB_TEST", 2, Registration.Create("a", "b", "c", "d", "e").Value, new string('c', 64), DateTimeOffset.UnixEpoch, "owner:x");

    [Fact]
    public async Task A_full_run_of_an_unregistered_version_is_refused_with_lab_not_registered_only_when_enforcement_is_on()
    {
        var refused = await Handler(enforceRegistration: true).HandleAsync(Full(), Ct);
        var allowed = await Handler(enforceRegistration: false).HandleAsync(Full(), Ct);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(LabErrors.NotRegisteredCode);
        LabErrors.IsUnprocessable(refused.Error.Code).ShouldBeTrue();
        allowed.IsSuccess.ShouldBeTrue();
        _appended.Count(r => r.TrialKind == "run").ShouldBe(1, "the refused post stored nothing");
    }

    [Fact]
    public async Task A_registered_version_runs_under_enforcement_and_a_quick_test_never_needs_one()
    {
        _registrations.GetAsync("LAB_TEST", 2, Arg.Any<CancellationToken>()).Returns(Registered());

        var full = await Handler(true).HandleAsync(Full(), Ct);
        full.IsSuccess.ShouldBeTrue(full.IsFailure ? full.Error.Message : string.Empty);
        (await Handler(true).HandleAsync(Full("quick"), Ct)).IsSuccess.ShouldBeTrue();
        _registrations.ClearReceivedCalls();
        (await Handler(true).HandleAsync(Full("quick"), Ct)).IsSuccess.ShouldBeTrue();
        await _registrations.DidNotReceiveWithAnyArgs().GetAsync(default!, 0, Ct);
    }

    [Fact]
    public async Task The_first_full_test_of_a_hash_adds_the_extra_exit_candidates_as_looks_and_a_later_one_adds_none()
    {
        await Handler().HandleAsync(Full(), Ct);
        _ledger.HasFullRunAsync("LAB_TEST", Hash, Arg.Any<CancellationToken>()).Returns(true);
        await Handler().HandleAsync(Full("robust"), Ct);

        _appended.Select(r => r.ExtraTrials).ShouldBe([3, 0], "four candidate exits are three extra looks");
    }

    [Fact]
    public async Task A_quick_run_adds_no_extra_looks_and_freezes_no_plan()
    {
        await Handler().HandleAsync(Full("quick"), Ct);

        _appended.Single().ExtraTrials.ShouldBe(0);
        await _freezes.DidNotReceiveWithAnyArgs().FreezeAsync(default!, 0, default!, Guid.Empty, default, Ct);
    }

    [Fact]
    public async Task A_changed_plan_appends_one_replan_row_and_the_same_plan_appends_nothing()
    {
        _freezes.FreezeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new PlanFreezeOutcome(true, 1), new PlanFreezeOutcome(false, 1));

        await Handler().HandleAsync(Full(), Ct);
        await Handler().HandleAsync(Full(), Ct);

        _appended.Count(r => r.TrialKind == "replan").ShouldBe(1);
        var replan = _appended.Single(r => r.TrialKind == "replan");
        replan.StrategyHash.ShouldBe(Hash);
        replan.RunId.ShouldNotBe(_appended.First(r => r.TrialKind == "run").RunId);
        replan.ExtraTrials.ShouldBe(0);
    }

    [Fact]
    public async Task The_plan_that_is_frozen_includes_the_registration_and_a_sweep_of_the_declared_parameter()
    {
        string? first = null;
        _freezes.FreezeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Do<string>(h => first ??= h), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new PlanFreezeOutcome(true, 0));
        await Handler().HandleAsync(Full(), Ct);
        var before = first;
        _registrations.GetAsync("LAB_TEST", 2, Arg.Any<CancellationToken>()).Returns(Registered());
        first = null;

        await Handler().HandleAsync(Full(), Ct);

        before.ShouldNotBeNull();
        first.ShouldNotBeNull().ShouldNotBe(before, "registering after a first unregistered run changes the plan, which is a new look");
    }

    [Fact]
    public async Task With_hold_out_enforcement_off_the_first_post_still_fixes_the_block_and_cuts_a_default_window_but_an_explicit_one_is_left_alone()
    {
        HoldOutBlock? stored = null;
        _holdOuts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => stored);
        _holdOuts.InsertBlockAsync(Arg.Any<HoldOutBlock>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            stored = ci.Arg<HoldOutBlock>();
            return true;
        });

        var cut = await Handler().HandleAsync(Full(), Ct);
        var explicitWindow = await Handler().HandleAsync(Full("full", new DateOnly(2026, 2, 1), new DateOnly(2026, 7, 1)), Ct);

        stored.ShouldNotBeNull().Start.ShouldBe(new DateOnly(2026, 5, 1));
        cut.Value.Run.Window.To.ShouldBe(new DateOnly(2026, 4, 30), "a development run never reads the hold-out, enforcement or not");
        explicitWindow.IsSuccess.ShouldBeTrue("only enforcement refuses an explicit window");
        explicitWindow.Value.Run.Window.To.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public async Task A_window_that_reaches_a_released_block_carries_its_seen_holdout_label_and_one_that_does_not_carries_none()
    {
        var hash = new string('d', 64);
        var one = new HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2026, 1, 5), new DateOnly(2026, 10, 30), 6, hash, DateTimeOffset.UnixEpoch);
        var two = new HoldOutBlock(new DateOnly(2026, 10, 31), one.HistoryFirst, new DateOnly(2027, 4, 30), 6, hash, DateTimeOffset.UnixEpoch, one.Start);
        _holdOuts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(two);
        _holdOuts.ListBlocksAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredBlock>>([new StoredBlock(one, new StoredRelease(one.Start, DateTimeOffset.UnixEpoch, "owner", "owner:x", hash, true, two.Start)), new StoredBlock(two, null)]));

        var reaching = await Handler().HandleAsync(Full(), Ct);
        var before = await Handler().HandleAsync(Full("full", new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30)), Ct);

        reaching.Value.Run.Labels.ShouldContain("seen_holdout:2026-05-01");
        before.Value.Run.Labels.ShouldNotContain(l => l.StartsWith("seen_holdout:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_guard_the_window_the_key_and_the_labels_are_what_they_were()
    {
        var plain = new StartLabRunHandler(_runs, _ledger, _strategies, _resolver, _baskets, _bars, _engine, _sheets, LabSettings.Default, _time, NullLogger<StartLabRunHandler>.Instance);

        var without = await plain.HandleAsync(Full(), Ct);

        without.Value.Run.Window.To.ShouldBe(new DateOnly(2026, 10, 30));
        without.Value.Run.Labels.ShouldNotContain(l => l.Contains("locked hold-out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_hold_out_enforcement_on_the_first_post_fixes_the_block_and_a_default_window_is_cut_and_says_so()
    {
        HoldOutBlock? stored = null;
        _holdOuts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => stored);
        _holdOuts.InsertBlockAsync(Arg.Any<HoldOutBlock>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            stored = ci.Arg<HoldOutBlock>();
            return true;
        });

        var result = await Handler(holdOut: new HoldOutOptions(Enforce: true)).HandleAsync(Full(), Ct);

        stored.ShouldNotBeNull().Start.ShouldBe(new DateOnly(2026, 5, 1), "the last session is 2026-10-30; six months back, plus a day");
        stored.LengthMonths.ShouldBe(6);
        result.Value.Run.Window.To.ShouldBe(new DateOnly(2026, 4, 30));
        result.Value.Run.Labels.ShouldContain(l => l.Contains("locked hold-out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_hold_out_enforcement_on_an_explicit_window_that_reaches_the_block_is_refused()
    {
        _holdOuts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2026, 1, 5), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UnixEpoch));

        var touching = await Handler(holdOut: new HoldOutOptions(Enforce: true)).HandleAsync(Full("full", new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 1)), Ct);
        var startsInside = await Handler(holdOut: new HoldOutOptions(Enforce: true)).HandleAsync(Full("full", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)), Ct);
        var before = await Handler(holdOut: new HoldOutOptions(Enforce: true)).HandleAsync(Full("full", new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30)), Ct);

        touching.Error.Code.ShouldBe(LabErrors.HoldOutTouchedCode);
        LabErrors.IsUnprocessable(touching.Error.Code).ShouldBeTrue();
        startsInside.Error.Code.ShouldBe(LabErrors.HoldOutTouchedCode);
        before.IsSuccess.ShouldBeTrue();
        before.Value.Run.Labels.ShouldNotContain(l => l.Contains("locked hold-out", StringComparison.Ordinal), "a window already before the block is not cut");
    }

    [Fact]
    public async Task A_quick_test_is_cut_too()
    {
        _holdOuts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2026, 1, 5), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UnixEpoch));

        var result = await Handler(holdOut: new HoldOutOptions(Enforce: true)).HandleAsync(Full("quick"), Ct);

        result.Value.Run.Window.To.ShouldBe(new DateOnly(2026, 4, 30));
    }
}
