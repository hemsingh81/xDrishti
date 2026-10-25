using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

public sealed class PlanVsActualTests
{
    private static PlanVsActualLine Graded(long id, string strategy, double plan, double slip, double charges, long qty, double entry, double stop, double fill) =>
        new(1, id, strategy, "owner", PlanVsActual.StatusGraded, plan, slip, charges, qty, entry, stop, fill, true, plan);

    private static PlanVsActualLine WithStatus(long id, string status, bool triggered = false, double? planR = null) =>
        new(1, id, "ORB", "owner", status, null, null, null, null, null, null, null, triggered, planR);

    private static List<PlanVsActualLine> Four() =>
    [
        Graded(1, "ORB", 2.0, 0.10, 40, 100, 500, 490, 1.80),
        Graded(2, "ORB", -1.0, 0.05, 30, 200, 250, 245, -1.12),
        Graded(3, "VWAP", 1.5, 0.20, 20, 50, 1000, 980, 1.20),
        Graded(4, "VWAP", 0.5, 0.00, 50, 100, 400, 395, 0.40),
    ];

    private static double Number(Metric m) => m.Number ?? throw new InvalidOperationException("no number");

    [Fact]
    public void The_hand_worked_chain_closes_on_the_four_tickets()
    {
        var r = PlanVsActual.Build(Four(), new PlanVsActualOptions(2));

        r.Chain.Select(s => s.Step).ShouldBe(["plan_prices", "entry_slippage", "charges", "not_attributed", "actual"]);
        Number(r.Chain[0].Metric).ShouldBe(0.75, 1e-9);
        Number(r.Chain[1].Metric).ShouldBe(-0.0875, 1e-9);
        Number(r.Chain[2].Metric).ShouldBe(-0.0475, 1e-9);
        Number(r.Chain[3].Metric).ShouldBe(-0.045, 1e-9);
        Number(r.Chain[4].Metric).ShouldBe(0.57, 1e-9);
        r.Chain.ShouldAllBe(s => s.N == 4);
        r.Counts.GradedOnFills.ShouldBe(4);
    }

    [Fact]
    public void By_strategy_matches_the_hand_table_and_is_insufficient_below_the_minimum()
    {
        var r = PlanVsActual.Build(Four(), new PlanVsActualOptions(2));
        var orb = r.ByStrategy[0];
        var vwap = r.ByStrategy[1];

        orb.StrategyId.ShouldBe("ORB");
        Number(orb.PlanR).ShouldBe(0.5, 1e-9);
        Number(orb.ActualR).ShouldBe(0.34, 1e-9);
        Number(orb.EntrySlippageR).ShouldBe(0.075, 1e-9);
        Number(vwap.PlanR).ShouldBe(1.0, 1e-9);
        Number(vwap.ActualR).ShouldBe(0.8, 1e-9);
        Number(vwap.EntrySlippageR).ShouldBe(0.10, 1e-9);

        var strict = PlanVsActual.Build(Four(), new PlanVsActualOptions(3));
        strict.ByStrategy.ShouldAllBe(s => s.PlanR.State == MetricState.Insufficient && s.PlanR.Number == null);
    }

    [Fact]
    public void Fewer_tickets_than_the_minimum_give_insufficient_with_no_number()
    {
        var r = PlanVsActual.Build(Four(), new PlanVsActualOptions(15));

        r.Chain.ShouldAllBe(s => s.Metric.State == MetricState.Insufficient && s.Metric.Number == null);
        r.Chain[0].Metric.Reason.ShouldBe("n 4 of at least 15");
    }

    [Fact]
    public void No_graded_ticket_gives_an_empty_chain_and_the_counts()
    {
        var r = PlanVsActual.Build([WithStatus(1, PlanVsActual.StatusNone), WithStatus(2, PlanVsActual.StatusNone)], new PlanVsActualOptions(2));

        r.Chain.ShouldAllBe(s => s.Metric.State == MetricState.Empty && s.Metric.Number == null);
        r.Counts.ApprovedLines.ShouldBe(2);
        r.Counts.WithoutFills.ShouldBe(2);
    }

    [Fact]
    public void Uncertain_no_levels_and_zero_risk_are_left_out_and_counted()
    {
        var lines = Four();
        lines.Add(WithStatus(5, PlanVsActual.StatusUncertain));
        lines.Add(WithStatus(6, PlanVsActual.StatusNoPlanLevels));
        lines.Add(Graded(7, "ORB", 1, 0, 10, 100, 500, 500, 1));
        lines.Add(WithStatus(8, PlanVsActual.StatusNotFinished));

        var r = PlanVsActual.Build(lines, new PlanVsActualOptions(2));

        r.Counts.ShouldBe(new PlanVsActualCounts(8, 8, 4, 1, 2, 1, 0));
        Number(r.Chain[4].Metric).ShouldBe(0.57, 1e-9);
    }

    [Fact]
    public void A_triggered_ticket_without_a_fill_is_a_missed_trigger_outside_the_chain()
    {
        var lines = Four();
        lines.Add(WithStatus(9, PlanVsActual.StatusNone, true, 1.0));
        lines.Add(WithStatus(10, PlanVsActual.StatusNone));

        var r = PlanVsActual.Build(lines, new PlanVsActualOptions(2));

        r.Counts.WithoutFills.ShouldBe(2);
        r.Missed.Count.ShouldBe(1);
        Number(r.Missed.TotalPlanR).ShouldBe(1.0, 1e-9);
        Number(r.Chain[0].Metric).ShouldBe(0.75, 1e-9);
    }

    [Fact]
    public void Exit_slippage_and_overrides_are_listed_as_not_decomposed()
    {
        PlanVsActual.Build([], new PlanVsActualOptions(2)).NotDecomposed.Select(n => n.Step).ShouldBe(["exit_slippage", "overrides"]);
    }

    [Fact]
    public void A_permutation_of_the_lines_changes_nothing()
    {
        var a = PlanVsActual.Build(Four(), new PlanVsActualOptions(2));
        var b = PlanVsActual.Build([.. Four().AsEnumerable().Reverse()], new PlanVsActualOptions(2));

        b.Chain.Select(s => s.Metric.Number).ShouldBe(a.Chain.Select(s => s.Metric.Number));
    }

    [Fact]
    public void The_chain_identity_holds_for_random_tickets()
    {
        var rnd = new Random(7);
        var lines = Enumerable.Range(1, 60).Select(i => Graded(i, "S", rnd.NextDouble() * 4 - 2, rnd.NextDouble() * 0.3, rnd.NextDouble() * 80, 10 + rnd.Next(200), 100 + rnd.NextDouble() * 400, 90 + rnd.NextDouble() * 5, rnd.NextDouble() * 4 - 2)).ToList();

        var r = PlanVsActual.Build(lines, new PlanVsActualOptions(2));

        var sum = Number(r.Chain[0].Metric) + Number(r.Chain[1].Metric) + Number(r.Chain[2].Metric) + Number(r.Chain[3].Metric);
        sum.ShouldBe(Number(r.Chain[4].Metric), 1e-9);
    }
}
