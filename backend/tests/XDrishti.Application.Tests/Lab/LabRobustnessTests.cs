using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>Spec P5-05 criteria 2, 5 and 6: the sweep runs the points as plain runs, a refused point is shown, each point is a ledger row, and the other checks read the out-of-sample trades.</summary>
public sealed class LabRobustnessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly List<RunSimulationQuery> _queries = [];

    private static LabRun Run() => new(
        Guid.NewGuid(), new string('1', 64), "LAB_TEST", 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(FabricatedRun.IdA, "AAA")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Running, "robustness", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static SimulationRequest Template() => new("LAB_TEST", new Dictionary<string, double>(), "15m", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), [FabricatedRun.IdA], null, CostChoice.Gross);

    private static FullTestOutcome Full(params double[] r) => new(
        [], new LabVerdictView("FAIL", 1, 1, new EvidenceView(0, null, null, 0, 0, null, 0, "walk_forward"), [], [], [], [], []), 1, 1, 0,
        [.. r.Select((v, i) => new SelectionTrade("c", new DateOnly(2026, 1, 1 + i), new DateOnly(2026, 1, 2 + i), v))]);

    private LabRobustness Service(LabSettings? settings = null)
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var q = ci.Arg<RunSimulationQuery>();
            _queries.Add(q);
            return q.Request.Parameters!.TryGetValue("bad", out _)
                ? Result.Failure<SimulationRun>(Error.Validation("simulation.unknown_parameter", "no such parameter"))
                : Result.Success(FabricatedRun.Standard());
        });
        return new LabRobustness(_simulation, settings ?? LabSettings.Default);
    }

    private static StrategyDescription Described(params (string Name, double Value)[] parameters) =>
        new("LAB_TEST", "intraday", new string('a', 64), "{}", [Timeframe.M15], [Side.Buy], [.. parameters.Select(p => new KeyValuePair<string, double>(p.Name, p.Value))]);

    [Fact]
    public async Task Each_sweep_point_is_a_plain_run_of_the_default_exit_with_one_changed_parameter_and_a_base_run_with_none()
    {
        var service = Service();
        var plan = service.Plan(Described(("period", 10), ("mult", 2.5)));

        var outcome = await service.RunAsync(Run(), Now, plan, Template(), Full(1, -1, 2), null, Ct);

        outcome.IsSuccess.ShouldBeTrue();
        _queries.Count.ShouldBe(1 + plan.Count);
        _queries[0].Request.Parameters.ShouldBeEmpty();
        _queries.ShouldAllBe(q => q.Request.Exits!.Candidates.Count == 1 && q.Request.Exits.Candidates[0].Id == "FIXED_RR{rr=2}");
        _queries.Skip(1).ShouldAllBe(q => q.Request.Parameters!.Count == 1);
        outcome.Value.View.Sensitivity.Parameters.Select(p => p.Param).ShouldBe(["period", "mult"]);
        outcome.Value.View.Sensitivity.Base.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_refused_point_is_shown_with_its_error_and_still_gets_a_ledger_row()
    {
        var service = Service();
        var plan = service.Plan(Described(("bad", 10)));

        var outcome = await service.RunAsync(Run(), Now, plan, Template(), Full(1, -1), null, Ct);

        outcome.Value.View.Sensitivity.Parameters.Single().Points.ShouldAllBe(p => p.Error == "no such parameter");
        outcome.Value.LedgerRows.Count.ShouldBe(plan.Count);
        plan.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_ledger_rows_are_sweep_variants_of_the_version_with_distinct_hashes_and_run_ids_and_the_same_on_a_retry()
    {
        var service = Service();
        var run = Run();
        var plan = service.Plan(Described(("period", 10), ("mult", 2.5)));

        var rows = (await service.RunAsync(run, Now, plan, Template(), Full(1, -1), null, Ct)).Value.LedgerRows;
        var again = (await service.RunAsync(run, Now, plan, Template(), Full(1, -1), null, Ct)).Value.LedgerRows;

        rows.ShouldAllBe(r => r.Method == "sweep" && r.ParentHash == run.StrategyHash && r.StrategyId == "LAB_TEST" && r.Version == 2);
        rows.Select(r => r.StrategyHash).Distinct().Count().ShouldBe(plan.Count);
        rows.Select(r => r.StrategyHash).ShouldNotContain(run.StrategyHash);
        rows.Select(r => r.RunId).Distinct().Count().ShouldBe(plan.Count);
        again.Select(r => r.RunId).ShouldBe(rows.Select(r => r.RunId));
    }

    [Fact]
    public async Task No_declared_parameter_means_no_sweep_but_the_other_checks_still_run()
    {
        var service = Service();

        var view = (await service.RunAsync(Run(), Now, service.Plan(Described()), Template(), Full(1, -1, 2, -1), null, Ct)).Value.View;

        view.Sensitivity.Parameters.ShouldBeEmpty();
        view.Sensitivity.Label.ShouldContain("no parameter");
        view.MonteCarlo!.Trades.ShouldBe(4);
        view.RandomBaseline!.Kind.ShouldBe("sign_flip");
        view.ByYear.Single().Year.ShouldBe(2026);
        view.ByRegime.ShouldStartWith("unavailable");
        view.Limits.ShouldContain(l => l.Contains("sign-flip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fewer_than_two_oos_trades_give_no_monte_carlo_and_say_so()
    {
        var service = Service();

        var view = (await service.RunAsync(Run(), Now, [], Template(), Full(1), null, Ct)).Value.View;

        view.MonteCarlo.ShouldBeNull();
        view.RandomBaseline.ShouldBeNull();
        view.Limits.ShouldContain(l => l.StartsWith("fewer than 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_same_run_gives_the_same_monte_carlo_and_another_run_key_another()
    {
        var service = Service();
        var run = Run();

        var a = (await service.RunAsync(run, Now, [], Template(), Full(1, -1, 2, -1, 3), null, Ct)).Value.View.MonteCarlo;
        var b = (await service.RunAsync(run, Now, [], Template(), Full(1, -1, 2, -1, 3), null, Ct)).Value.View.MonteCarlo;
        var other = (await service.RunAsync(run with { Key = new string('2', 64) }, Now, [], Template(), Full(1, -1, 2, -1, 3), null, Ct)).Value.View.MonteCarlo;

        a.ShouldBe(b);
        other!.Seed.ShouldNotBe(a!.Seed);
    }
}
