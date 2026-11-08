using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-06, D5: the one plain pass over the hold-out takes, per known cell, the trades of the exit the development test chose; a cell the development did not know is left out; a missing sheet or text refuses.</summary>
public sealed class HoldOutPassTests
{
    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IQueryHandler<RunComparisonQuery, Result<ComparisonRun>> _comparisons = Substitute.For<IQueryHandler<RunComparisonQuery, Result<ComparisonRun>>>();
    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly IStrategyChecker _checker = Substitute.For<IStrategyChecker>();
    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();
    private readonly IPlanFreezeStore _freezes = Substitute.For<IPlanFreezeStore>();
    private readonly IRegistrationStore _registrations = Substitute.For<IRegistrationStore>();
    private readonly List<RunComparisonQuery> _asked = [];

    private static readonly HoldOutBlock Block = new(new DateOnly(2026, 5, 1), new DateOnly(2024, 10, 1), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UnixEpoch);

    private static LabRun Run() => new(Guid.NewGuid(), new string('k', 64), "LAB_TEST", 1, Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(FabricatedRun.IdA, "AAA")], false, null),
        new DateOnly(2024, 10, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Succeeded, null, 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, null, null);

    private static LabVerdictView Verdict(params CellView[] cells) => new("PASS", 1, 1, new EvidenceView(100, 0.2, 1.5, 6, 5, 3, 0, "walk_forward"), [], [], cells, [], []);

    private static CellView Cell(string timeframe, string side, string chosen) => new("c", timeframe, side, "x", chosen, null, true, "ok", 5, 5, 50, 0.2, 0.1);

    public HoldOutPassTests()
    {
        _freezes.FirstAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _strategies.GetByHashAsync(Hash, Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 1, 1, Hash, "the text", "{}", null, null, DateTimeOffset.UnixEpoch));
        _checker.Check("the text").Returns(new StrategyCheck(new StrategyDescription("LAB_TEST", "intraday", Hash, "{}", [Timeframe.M15], [Side.Buy], []), 1, []));
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        var candidates = ExitFlags.Parse([.. LabSettings.Default.Exits], null, [])!.Candidates
            .Select((exit, i) => new CandidateRun(exit.Id, exit, i == 0 ? FabricatedRun.CostedStandard() : FabricatedRun.CostedStandard() with { Trades = [] }, 0))
            .ToList();
        _comparisons.HandleAsync(Arg.Do<RunComparisonQuery>(_asked.Add), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ComparisonRun(candidates, ComparisonReport.Build(candidates, true), new string('e', 64))));
    }

    private LabPreregistration Preregistration() => new(_registrations, _freezes, Substitute.For<ITestLedger>(), _sheets, _checker, LabSettings.Default, new RegistrationOptions());

    private async Task FreezeAsync()
    {
        var hash = await Preregistration().PlanHashAsync(Run(), new StoredVersion("LAB_TEST", 1, 1, Hash, "the text", "{}", null, null, DateTimeOffset.UnixEpoch), Ct);
        _freezes.FirstAsync("LAB_TEST", 1, Arg.Any<CancellationToken>()).Returns(hash);
    }

    private HoldOutPass Pass() => new(_comparisons, _strategies, _checker, _sheets, LabSettings.Default, Preregistration(), _freezes);

    [Fact]
    public async Task The_pass_runs_only_the_frozen_plan_and_refuses_a_plan_that_differs_from_the_first_freeze()
    {
        var run = Run();
        var version = new StoredVersion("LAB_TEST", 1, 1, Hash, "the text", "{}", null, null, DateTimeOffset.UnixEpoch);
        var frozenHash = await Preregistration().PlanHashAsync(run, version, Ct);
        _freezes.FirstAsync("LAB_TEST", 1, Arg.Any<CancellationToken>()).Returns(frozenHash);
        var same = await Pass().RunAsync(run, Verdict(Cell("15m", "BUY", DefaultId())), Block, Ct);
        _asked.Clear();
        _registrations.GetAsync("LAB_TEST", 1, Arg.Any<CancellationToken>()).Returns(new StoredRegistration("LAB_TEST", 1, Registration.Create("a", "b", "c", "d", "e").Value, new string('c', 64), DateTimeOffset.UnixEpoch, "owner:x"));

        var replanned = await Pass().RunAsync(run, Verdict(Cell("15m", "BUY", DefaultId())), Block, Ct);

        same.IsSuccess.ShouldBeTrue();
        replanned.IsFailure.ShouldBeTrue();
        replanned.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _asked.ShouldBeEmpty("a changed plan is not run on the hold-out");
    }


    private static string DefaultId() => ExitFlags.Parse([.. LabSettings.Default.Exits], null, [])!.Candidates[0].Id;

    private static string OtherId() => ExitFlags.Parse([.. LabSettings.Default.Exits], null, [])!.Candidates[1].Id;

    [Fact]
    public async Task The_pass_walks_the_candidates_over_exactly_the_block_costed_with_the_sheet()
    {
        await FreezeAsync();
        var result = await Pass().RunAsync(Run(), Verdict(Cell("15m", "BUY", DefaultId())), Block, Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var request = _asked.Single().Request;
        (request.From, request.To).ShouldBe((Block.Start, Block.HistoryLast));
        request.Costs.ShouldBeOfType<CostChoice.SheetChoice>();
        request.Exits.ShouldNotBeNull().Candidates.Count.ShouldBeGreaterThan(1);
        (result.Value.From, result.Value.To).ShouldBe((Block.Start, Block.HistoryLast));
    }

    [Fact]
    public async Task The_trades_are_those_of_the_chosen_exit_of_each_known_cell()
    {
        await FreezeAsync();
        var chosenDefault = await Pass().RunAsync(Run(), Verdict(Cell("15m", "BUY", DefaultId())), Block, Ct);
        var chosenEmpty = await Pass().RunAsync(Run(), Verdict(Cell("15m", "BUY", OtherId())), Block, Ct);

        chosenDefault.Value.Trades.ShouldBeGreaterThan(0);
        chosenDefault.Value.MeanR.ShouldNotBeNull();
        chosenEmpty.Value.Trades.ShouldBe(0, "the other candidate had no trades");
        chosenEmpty.Value.MeanR.ShouldBeNull("no trades is no figure, never zero");
    }

    [Fact]
    public async Task A_cell_the_development_test_did_not_know_is_not_in_the_plan()
    {
        await FreezeAsync();
        var result = await Pass().RunAsync(Run(), Verdict(Cell("5m", "BUY", DefaultId()), Cell("5m", "SELL", DefaultId())), Block, Ct);

        result.Value.Trades.ShouldBe(0);
    }

    [Fact]
    public async Task A_missing_text_a_missing_sheet_and_a_refused_comparison_each_refuse_without_a_number()
    {
        await FreezeAsync();
        _strategies.GetByHashAsync(Hash, Arg.Any<CancellationToken>()).Returns((StoredVersion?)null);
        (await Pass().RunAsync(Run(), Verdict(), Block, Ct)).Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _strategies.GetByHashAsync(Hash, Arg.Any<CancellationToken>()).Returns(new StoredVersion("LAB_TEST", 1, 1, Hash, "the text", "{}", null, null, DateTimeOffset.UnixEpoch));
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<CostSheet>(Error.NotFound("costs.sheet_not_found", "none")));
        (await Pass().RunAsync(Run(), Verdict(), Block, Ct)).Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FixtureSheet.Sheet));
        _comparisons.HandleAsync(Arg.Any<RunComparisonQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ComparisonRun>(Error.Validation("x.y", "refused")));

        (await Pass().RunAsync(Run(), Verdict(), Block, Ct)).Error.Code.ShouldBe("x.y");
    }

    [Fact]
    public async Task A_version_with_no_frozen_plan_is_refused_and_nothing_is_run()
    {
        var result = await Pass().RunAsync(Run(), Verdict(Cell("15m", "BUY", DefaultId())), Block, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        result.Error.Message.ShouldContain("no frozen plan");
        _asked.ShouldBeEmpty("the plan hash is never skipped");
    }

    [Fact]
    public async Task A_chosen_exit_that_is_not_among_the_candidates_is_refused_instead_of_falling_back_to_the_first()
    {
        await FreezeAsync();

        var result = await Pass().RunAsync(Run(), Verdict(Cell("15m", "BUY", "NO_SUCH_EXIT:x=1")), Block, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        result.Error.Message.ShouldContain("not among the candidates");
    }

    [Fact]
    public async Task A_conservative_run_is_judged_on_conservative_fills_and_a_default_run_on_default_fills()
    {
        await FreezeAsync();
        var conservative = Run() with { RunOptions = "slippage=conservative" };
        var cell = Verdict(Cell("15m", "BUY", DefaultId()));

        (await Pass().RunAsync(conservative, cell, Block, Ct)).IsSuccess.ShouldBeTrue();
        (await Pass().RunAsync(Run(), cell, Block, Ct)).IsSuccess.ShouldBeTrue();

        var fills = _asked[0].Request.Fills.ShouldNotBeNull();
        fills.ConservativeSlippage.ShouldBeTrue("the hold-out must be judged on the fills the development was");
        fills.BarRangeFrac.ShouldBe(TieredSlippage.BarRangeFrac);
        var plain = _asked[1].Request.Fills.ShouldNotBeNull();
        plain.ConservativeSlippage.ShouldBeFalse();
        plain.BarRangeFrac.ShouldBe(FillRules.Default.Slippage.BarRangeFrac);
    }
}
