using System.Text.Json;
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

/// <summary>CF-03 part B (ADR 0228): the robustness stage keeps the daily series of the base and each sweep point (net R by exit date) and gives PBO beside the sweep; nothing of the old block changes.</summary>
public sealed class LabRobustnessPboTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private static LabRun Run() => new(
        Guid.NewGuid(), new string('1', 64), "LAB_TEST", 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(FabricatedRun.IdA, "AAA")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Running, "robustness", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static SimulationRequest Template() => new("LAB_TEST", new Dictionary<string, double>(), "15m", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), [FabricatedRun.IdA], null, CostChoice.Gross);

    private static FullTestOutcome Full() => new(
        [], new LabVerdictView("FAIL", 1, 1, new EvidenceView(0, null, null, 0, 0, null, 0, "walk_forward"), [], [], [], [], []), 1, 1, 0,
        [.. Enumerable.Range(0, 12).Select(i => new SelectionTrade("c", new DateOnly(2026, 1, 1).AddDays(i), new DateOnly(2026, 1, 2).AddDays(i), i % 3 == 0 ? -1 : 0.8))]);

    private static StrategyDescription Described(int parameters) =>
        new("LAB_TEST", "intraday", new string('a', 64), "{}", [Timeframe.M15], [Side.Buy], [.. new[] { ("a", 10.0), ("b", 20.0), ("c", 30.0) }.Take(parameters).Select(p => new KeyValuePair<string, double>(p.Item1, p.Item2))]);

    /// <summary>A run of 40 trades on 40 consecutive dates whose results depend on the parameter text, so every variant differs and the same variant is the same each time.</summary>
    private static SimulationRun RunOf(IReadOnlyDictionary<string, double> parameters, int trades = 40)
    {
        var key = string.Join(";", parameters.Select(p => p.Key + "=" + p.Value));
        var random = new SeededRandom(key.Aggregate(14695981039346656037UL, (h, c) => unchecked((h ^ c) * 1099511628211UL)));
        var list = Enumerable.Range(0, trades)
            .Select(i =>
            {
                var r = Math.Round(random.NextDouble() - 0.45, 3);
                return FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday.AddDays(i), rExact: r, rFilled: r);
            })
            .ToList();
        return FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday.AddDays(60))], list);
    }

    private static Dictionary<string, double> Params(RunSimulationQuery q) => q.Request.Parameters is { } p ? new Dictionary<string, double>(p) : [];

    private LabRobustness Service(PboOptions? pbo = null, Func<RunSimulationQuery, Result<SimulationRun>>? runs = null)
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => (runs ?? (q => Result.Success(RunOf(Params(q)))))(ci.Arg<RunSimulationQuery>()));
        return new LabRobustness(_simulation, LabSettings.Default, null, null, null, pbo);
    }

    private static async Task<RobustnessOutcome> RunAsync(LabRobustness service, int parameters, LabRun? run = null) =>
        (await service.RunAsync(run ?? Run(), Now, service.Plan(Described(parameters)), Template(), Full(), null, Ct)).Value;

    [Fact]
    public async Task Three_swept_parameters_give_thirteen_variants_a_figure_and_the_series_of_each()
    {
        var service = Service();
        service.Plan(Described(3)).Points.Count.ShouldBe(12);

        var view = (await RunAsync(service, 3)).View;

        view.Pbo!.Variants.ShouldBe(13);
        view.Pbo.Splits.ShouldBe(70);
        view.Pbo.Pbo.ShouldNotBeNull().ShouldBeInRange(0.0, 1.0);
        view.Pbo.Note.ShouldNotStartWith("unavailable");
        var series = view.VariantSeries!;
        series.Variants.Count.ShouldBe(13);
        series.Variants[0].Label.ShouldBe("base");
        series.Variants.Skip(1).ShouldAllBe(v => v.Label.Contains('=', StringComparison.Ordinal));
        series.Dates.Count.ShouldBe(40, "one date per session of the invented trades, the union over the variants");
        series.Dates.ShouldBe(series.Dates.Order().ToList());
        series.Variants.ShouldAllBe(v => v.Values.Count == 40 && v.Values.All(x => Math.Round(x, 4) == x));
        series.Variants.Select(v => v.Values.Sum()).Distinct().Count().ShouldBe(13, "every variant has its own results");
    }

    [Fact]
    public async Task The_figure_is_the_one_the_stored_series_give()
    {
        var view = (await RunAsync(Service(), 3)).View;

        var again = Cscv.Pbo([.. view.VariantSeries!.Variants.Select(v => v.Values.ToArray())], 8);

        again.Pbo.ShouldBe(view.Pbo!.Pbo);
        again.MedianLogit.ShouldBe(view.Pbo.MedianLogit);
    }

    [Fact]
    public async Task Nine_variants_read_unavailable_and_the_outcome_block_is_otherwise_the_same()
    {
        var on = (await RunAsync(Service(), 2)).View;
        var off = (await RunAsync(Service(new PboOptions(ConfidenceMode.Off)), 2)).View;

        on.Pbo!.Note.ShouldStartWith("unavailable: needs at least 10 variants (have 9)");
        on.Pbo.Pbo.ShouldBeNull();
        on.VariantSeries!.Variants.Count.ShouldBe(9);
        off.Pbo.ShouldBeNull();
        off.VariantSeries.ShouldBeNull();
        Json(on with { Pbo = null, VariantSeries = null }).ShouldBe(Json(off), "every existing member of the block is byte for byte the same");
    }

    [Fact]
    public async Task Off_leaves_every_existing_member_of_the_block_byte_for_byte_as_it_was_with_the_figure_on()
    {
        var run = Run();
        var on = (await RunAsync(Service(), 3, run)).View;
        var off = (await RunAsync(Service(new PboOptions(ConfidenceMode.Off)), 3, run)).View;

        off.Pbo.ShouldBeNull();
        on.Pbo.ShouldNotBeNull();
        Json(on with { Pbo = null, VariantSeries = null }).ShouldBe(Json(off));
        on.MonteCarlo.ShouldBe(off.MonteCarlo);
        on.MonteCarloBlock.ShouldBe(off.MonteCarloBlock);
        on.Sensitivity.Base.ShouldBe(off.Sensitivity.Base);
    }

    [Fact]
    public async Task The_ledger_rows_and_the_statistics_of_the_sweep_do_not_depend_on_the_figure()
    {
        var run = Run();
        var on = await RunAsync(Service(), 3, run);
        var off = await RunAsync(Service(new PboOptions(ConfidenceMode.Off)), 3, run);

        on.LedgerRows.ShouldBe(off.LedgerRows);
        on.VariantStats.ShouldBe(off.VariantStats);
    }

    [Fact]
    public async Task A_point_that_is_refused_or_ran_no_trade_is_left_out_and_counted()
    {
        var calls = 0;
        var service = Service(runs: q =>
        {
            calls++;
            return calls switch
            {
                3 => Result.Failure<SimulationRun>(Error.Validation("sim.refused", "no")),
                5 => Result.Success(RunOf(Params(q), trades: 0)),
                _ => Result.Success(RunOf(Params(q))),
            };
        });

        var view = (await RunAsync(service, 3)).View;

        view.Pbo!.Variants.ShouldBe(11);
        view.Pbo.Note.ShouldContain("2 run(s) of the base and the sweep ran no trade or were refused");
        view.VariantSeries!.Variants.Count.ShouldBe(11);
    }

    [Fact]
    public async Task A_base_run_that_failed_is_left_out_and_the_rest_still_count()
    {
        var service = Service(runs: q => Params(q).Count == 0 ? Result.Failure<SimulationRun>(Error.Validation("sim.refused", "no")) : Result.Success(RunOf(Params(q))));

        var view = (await RunAsync(service, 3)).View;

        view.Pbo!.Variants.ShouldBe(12);
        view.VariantSeries!.Variants[0].Label.ShouldNotBe("base");
    }

    [Fact]
    public async Task A_result_that_is_not_a_finite_number_refuses_the_series_and_never_gives_a_number()
    {
        var service = Service(runs: q => Result.Success(Params(q).TryGetValue("b", out var b) && b > 21 ? WithNaN(Params(q)) : RunOf(Params(q))));

        var view = (await RunAsync(service, 3)).View;

        view.Pbo!.Note.ShouldStartWith("unavailable: a daily value is not a finite number");
        view.Pbo.Pbo.ShouldBeNull();
        view.VariantSeries.ShouldBeNull();
    }

    private static SimulationRun WithNaN(IReadOnlyDictionary<string, double> parameters)
    {
        var good = RunOf(parameters, trades: 2);
        var bad = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday.AddDays(3), rExact: 1, rFilled: double.NaN);
        return FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday.AddDays(60))], [.. good.Trades.Select(t => t.Trade), bad]);
    }

    [Fact]
    public async Task The_settings_are_used_and_recorded_with_the_figure_and_gate_mode_changes_nothing_in_the_block()
    {
        var run = Run();
        var gate = (await RunAsync(Service(new PboOptions(ConfidenceMode.Gate, 6, 12, 0.35, PboMetric.Sharpe)), 3, run)).View;
        var report = (await RunAsync(Service(new PboOptions(ConfidenceMode.ReportOnly, 6, 12, 0.35, PboMetric.Sharpe)), 3, run)).View;

        gate.Pbo!.Settings.ShouldBe("Gate/6/12/0.35/Sharpe");
        gate.Pbo.Blocks.ShouldBe(6);
        gate.Pbo.Splits.ShouldBe(20);
        gate.Pbo.Metric.ShouldBe("Sharpe");
        gate.Pbo.MaxPbo.ShouldBe(0.35);
        gate.Pbo.Capped.ShouldBeFalse("capping is the runner's act, on the verdict");
        gate.Pbo.Pbo.ShouldBe(report.Pbo!.Pbo);
    }

    [Fact]
    public async Task The_same_run_gives_the_same_figure_and_series()
    {
        var run = Run();
        var service = Service();

        var a = (await RunAsync(service, 3, run)).View;
        var b = (await RunAsync(service, 3, run)).View;

        a.Pbo.ShouldBe(b.Pbo);
        Json(a).ShouldBe(Json(b));
    }

    [Fact]
    public async Task A_document_written_before_the_figure_reads_without_it_and_one_with_it_round_trips()
    {
        var view = (await RunAsync(Service(), 3)).View;
        var web = Web;
        var oldText = JsonSerializer.Serialize(view, web).Replace(",\"pbo\":", ",\"x\":", StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<RobustnessView>(JsonSerializer.Serialize(view, web), web)!;
        var old = JsonSerializer.Deserialize<RobustnessView>(oldText, web)!;

        back.Pbo.ShouldBe(view.Pbo);
        back.VariantSeries!.Dates.ShouldBe(view.VariantSeries!.Dates);
        old.Pbo.ShouldBeNull("a result stored before reads not computed");
    }

    private static string Json(RobustnessView view) => JsonSerializer.Serialize(view);
}
