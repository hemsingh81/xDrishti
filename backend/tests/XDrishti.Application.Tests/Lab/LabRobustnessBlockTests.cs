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

/// <summary>CF-03 (ADR 0205): the stationary block bootstrap beside the Monte Carlo in the robustness block, and the statistics of each sweep point that feed the deflated Sharpe ratio.</summary>
public sealed class LabRobustnessBlockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 6, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private static LabRun Run() => new(
        Guid.NewGuid(), new string('1', 64), "LAB_TEST", 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(FabricatedRun.IdA, "AAA")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Running, "robustness", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static SimulationRequest Template() => new("LAB_TEST", new Dictionary<string, double>(), "15m", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), [FabricatedRun.IdA], null, CostChoice.Gross);

    private static FullTestOutcome Full(IReadOnlyList<SelectionTrade> trades) => new(
        [], new LabVerdictView("FAIL", 1, 1, new EvidenceView(0, null, null, 0, 0, null, 0, "walk_forward"), [], [], [], [], []), 1, 1, 0, trades);

    private static SelectionTrade[] Daily(params double[] r) => [.. r.Select((v, i) => new SelectionTrade("c", new DateOnly(2026, 1, 1).AddDays(i), new DateOnly(2026, 1, 2).AddDays(i), v))];

    private LabRobustness Service(BootstrapOptions? bootstrap = null, LabSettings? settings = null)
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FabricatedRun.Standard()));
        return new LabRobustness(_simulation, settings ?? LabSettings.Default, null, bootstrap);
    }

    private static StrategyDescription Described(params (string Name, double Value)[] parameters) =>
        new("LAB_TEST", "intraday", new string('a', 64), "{}", [Timeframe.M15], [Side.Buy], [.. parameters.Select(p => new KeyValuePair<string, double>(p.Name, p.Value))]);

    private static async Task<RobustnessOutcome> RunAsync(LabRobustness service, IReadOnlyList<SelectionTrade> trades, StrategyDescription? description = null) =>
        (await service.RunAsync(Run(), Now, service.Plan(description ?? Described()), Template(), Full(trades), null, Ct)).Value;

    [Fact]
    public async Task The_block_result_sits_beside_the_monte_carlo_with_both_widths_and_their_ratio()
    {
        var outcome = await RunAsync(Service(), Daily(1, -1, 2, -0.5, 1.5, -1, 0.5, 2, -1, 1));

        var block = outcome.View.MonteCarloBlock!;
        var mc = outcome.View.MonteCarlo!;
        block.Mode.ShouldBe("ReportOnly");
        block.Sessions.ShouldBe(10);
        block.BlockLength.ShouldBe(2, "round(10^(1/3)) = 2");
        block.Draws.ShouldBe(LabSettings.Default.Robustness.McDraws);
        block.ObservedMeanR!.Value.ShouldBe(mc.ObservedMeanR, 1e-12);
        block.WidthRatioToIid!.Value.ShouldBe((block.P95!.Value - block.P05!.Value) / (mc.MeanP95 - mc.MeanP05), 1e-12);
        block.Seed.ShouldNotBe(mc.Seed, "a salt makes the streams differ");
        block.Settings.ShouldBe($"ReportOnly/{block.Draws}/auto");
        block.Note.ShouldContain("not trustworthy", Case.Insensitive);
        block.Note.ShouldContain("independent");
    }

    [Fact]
    public async Task The_old_monte_carlo_member_is_the_same_with_the_bootstrap_on_or_off()
    {
        var trades = Daily(1, -1, 2, -0.5, 1.5, -1);

        var on = await RunAsync(Service(), trades);
        var off = await RunAsync(Service(new BootstrapOptions(ConfidenceMode.Off)), trades);

        off.View.MonteCarloBlock.ShouldBeNull();
        on.View.MonteCarlo.ShouldBe(off.View.MonteCarlo);
        Json(on.View with { MonteCarloBlock = null }).ShouldBe(Json(off.View), "every other member of the block is byte for byte the same");
    }

    private static string Json(RobustnessView view) => System.Text.Json.JsonSerializer.Serialize(view);

    [Fact]
    public async Task The_same_run_gives_the_same_block_numbers()
    {
        var trades = Daily(1, -1, 2, -0.5, 1.5, -1);
        var run = Run();
        var service = Service();
        var plan = service.Plan(Described());

        var a = (await service.RunAsync(run, Now, plan, Template(), Full(trades), null, Ct)).Value.View.MonteCarloBlock;
        var b = (await service.RunAsync(run, Now, plan, Template(), Full(trades), null, Ct)).Value.View.MonteCarloBlock;

        a.ShouldBe(b);
    }

    [Fact]
    public async Task Trades_of_one_entry_day_are_one_session()
    {
        var day1 = new DateOnly(2026, 1, 5);
        var day2 = new DateOnly(2026, 1, 6);
        var trades = new[]
        {
            new SelectionTrade("c", day1, day1, 1), new SelectionTrade("c", day1, day1, -1), new SelectionTrade("c", day1, day1, 2),
            new SelectionTrade("c", day2, day2, 0.5), new SelectionTrade("c", day2, day2, 0.5),
        };

        var block = (await RunAsync(Service(), trades)).View.MonteCarloBlock!;

        block.Sessions.ShouldBe(2);
        block.ObservedMeanR!.Value.ShouldBe(3.0 / 5, 1e-12);
    }

    [Fact]
    public async Task One_session_or_none_is_unavailable_with_a_reason_and_no_number()
    {
        var oneDay = new DateOnly(2026, 1, 5);

        var block = (await RunAsync(Service(), [new SelectionTrade("c", oneDay, oneDay, 1), new SelectionTrade("c", oneDay, oneDay, -1)])).View.MonteCarloBlock!;
        var none = (await RunAsync(Service(), [])).View.MonteCarloBlock!;

        foreach (var v in new[] { block, none })
        {
            v.Note.ShouldStartWith("unavailable: fewer than 2 sessions");
            v.P05.ShouldBeNull();
            v.P95.ShouldBeNull();
            v.ObservedMeanR.ShouldBeNull();
            v.WidthRatioToIid.ShouldBeNull();
            v.BlockLength.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_configured_block_length_and_draws_are_used_and_recorded()
    {
        var block = (await RunAsync(Service(new BootstrapOptions(ConfidenceMode.ReportOnly, 400, 3)), Daily(1, -1, 2, -0.5, 1.5, -1))).View.MonteCarloBlock!;

        block.BlockLength.ShouldBe(3);
        block.Draws.ShouldBe(400);
        block.Settings.ShouldBe("ReportOnly/400/3");
    }

    [Fact]
    public async Task Thirty_sessions_carry_no_trust_warning()
    {
        var block = (await RunAsync(Service(), Daily([.. Enumerable.Range(0, 30).Select(i => i % 3 == 0 ? -1.0 : 0.8)]))).View.MonteCarloBlock!;

        block.Sessions.ShouldBe(30);
        block.Note.ShouldNotContain("not trustworthy");
    }

    [Fact]
    public async Task Each_sweep_point_that_ran_trades_gets_its_statistics_under_the_ledger_rows_run_id_and_hash()
    {
        var service = Service();
        var plan = service.Plan(Described(("period", 10), ("mult", 2.5)));

        var outcome = (await service.RunAsync(Run(), Now, plan, Template(), Full(Daily(1, -1, 2)), null, Ct)).Value;

        outcome.VariantStats.Count.ShouldBe(plan.Points.Count);
        plan.Points.Count.ShouldBeGreaterThan(0);
        outcome.VariantStats.Select(s => (s.RunId, s.VariantHash)).ShouldBe(outcome.LedgerRows.Select(r => (r.RunId, r.StrategyHash)));
        outcome.VariantStats.ShouldAllBe(s => s.Method == "sweep" && s.StrategyId == "LAB_TEST" && s.N == FabricatedRun.Standard().Trades.Count && s.CreatedAt == Now);
    }
}
