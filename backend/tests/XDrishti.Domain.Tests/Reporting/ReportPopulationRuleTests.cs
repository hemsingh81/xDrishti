using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>The population rule (P8-00 D2, ADR 0099): no overlapping runs, no trade twice, no gross and net mixed, no net before the owner confirms the sheet.</summary>
public sealed class ReportPopulationRuleTests
{
    private static Result<ReportPopulation> Create(IReadOnlyList<ReportRun> runs, IReadOnlyList<ReportTrade> trades, ReportOptions? options = null) =>
        ReportPopulation.Create(runs, trades, options ?? Small());

    [Fact]
    public void Two_runs_of_one_strategy_universe_window_and_labels_are_refused()
    {
        var result = Create([Run("r1"), Run("r2")], [.. Series("r1", "ORB", Repeat(1, 3)), .. Series("r2", "ORB", Repeat(1, 3), firstDay: 10)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("reports.run_overlap");
        result.Error.Message.ShouldContain("'r1'");
        result.Error.Message.ShouldContain("'r2'");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void Runs_of_one_strategy_in_windows_that_do_not_overlap_are_allowed()
    {
        var result = Create([Run("r1", startOffset: 0, endOffset: 99), Run("r2", startOffset: 100, endOffset: 199)], [.. Series("r1", "ORB", Repeat(1, 3)), .. Series("r2", "ORB", Repeat(1, 3), firstDay: 100).Select(t => t with { TradeKey = "later-" + t.TradeKey })]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TradeCount.ShouldBe(6);
    }

    [Fact]
    public void Runs_that_share_a_window_but_hold_different_strategies_are_allowed()
    {
        var result = Create([Run("r1"), Run("r2")], [.. Series("r1", "ORB", Repeat(1, 3)), .. Series("r2", "VWAP", Repeat(1, 3))]);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_same_run_listed_twice_is_one_run_and_the_same_trade_twice_in_a_run_is_counted_once()
    {
        var trades = Series("r1", "ORB", [1, -1, 1]);

        var result = Create([Run("r1"), Run("r1")], [.. trades, trades[1]]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TradeCount.ShouldBe(3);
        result.Value.Evidence.DuplicateTradesDropped.ShouldBe(1);
        result.Value.Evidence.Population.RunKeys.ShouldBe(["r1"]);
    }

    [Fact]
    public void A_trade_twice_in_a_run_with_different_values_is_refused_whatever_the_order()
    {
        var a = Trade("r1", "T1", 1);
        var b = Trade("r1", "T1", 2);

        Create([Run("r1")], [a, b]).Error.Code.ShouldBe("reports.invalid");
        Create([Run("r1")], [b, a]).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void A_run_listed_twice_with_different_definitions_is_refused()
    {
        var result = Create([Run("r1"), Run("r1", exit: "TRAIL")], []);

        result.Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void One_signal_in_two_runs_of_different_universes_and_the_same_labels_is_counted_once_by_refusing_the_second_run()
    {
        var result = Create([Run("r1", universe: "U1"), Run("r2", universe: "U2")], [Trade("r1", "ORB|AAA|BUY|2026-01-05", 1), Trade("r2", "ORB|AAA|BUY|2026-01-05", 1)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("reports.run_overlap");
        result.Error.Message.ShouldContain("ORB|AAA|BUY|2026-01-05");
    }

    [Fact]
    public void Gross_and_net_runs_are_never_mixed()
    {
        var result = Create(
            [Run("r1", costed: true), Run("r2", costed: false)],
            [Trade("r1", "A", 1, strategy: "ORB", net: 0.9), Trade("r2", "B", 1, strategy: "VWAP")],
            Small());

        result.Error.Code.ShouldBe("reports.mixed_basis");
        result.Error.Message.ShouldContain("r1");
        result.Error.Message.ShouldContain("r2");
    }

    [Fact]
    public void A_net_report_is_refused_until_the_owner_has_confirmed_the_charge_sheet()
    {
        var runs = new[] { Run("r1", costed: true) };
        var trades = new[] { Trade("r1", "A", 1, net: 0.9) };

        Create(runs, trades, new ReportOptions { Basis = ReportBasis.Net }).Error.Code.ShouldBe("reports.net_not_confirmed");
        Create(runs, trades, new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = null }).IsFailure.ShouldBeTrue();
        Create(runs, trades, new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = new ChargeSheetConfirmation(" ", new string('a', 64), new DateOnly(2026, 10, 1)) }).Error.Code.ShouldBe("reports.invalid");
        Create(runs, trades, new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = new ChargeSheetConfirmation("sheet-1", "not-a-hash", new DateOnly(2026, 10, 1)) }).Error.Code.ShouldBe("reports.invalid");
        Create(runs, trades, new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = new ChargeSheetConfirmation("sheet-1", string.Empty, new DateOnly(2026, 10, 1)) }).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void A_confirmed_net_report_uses_the_net_r_and_says_net()
    {
        var runs = new[] { Run("r1", costed: true) };
        var trades = new[] { Trade("r1", "A", 1, net: 0.75), Trade("r1", "B", -1, net: -1.25, day: 1) };
        var options = new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = Confirmed, Stats = Small(1).Stats, MinWins = 1, MinNonWins = 1 };

        var population = Create(runs, trades, options).Value;

        var group = Only(Group(population), "all");
        group.TotalR.Number!.Value.ShouldBe(-0.5, 1e-12);
        group.Evidence.Basis.ShouldBe(ReportBasis.Net);
        population.Evidence.Limits.ShouldContain(l => l.Contains("net of the stored charge sheet: charge sheet 'sheet-1' with rates aaaaaaaaaaaa confirmed by the owner on 2026-10-01", StringComparison.Ordinal));
        population.Evidence.NetBasisConfirmation.ShouldBe(Confirmed);
    }

    [Fact]
    public void A_gross_report_over_costed_runs_uses_the_filled_r_and_says_gross()
    {
        var population = Create([Run("r1", costed: true)], [Trade("r1", "A", 1, net: 0.75)], Small(1)).Value;

        var group = Only(Group(population), "all");

        group.TotalR.Number!.Value.ShouldBe(1, 1e-12);
        group.Evidence.Basis.ShouldBe(ReportBasis.Gross);
        population.Evidence.Limits.ShouldContain(l => l.Contains("R is gross", StringComparison.Ordinal));
        population.Evidence.NetBasisConfirmation.ShouldBeNull();
    }

    [Fact]
    public void A_confirmed_net_report_over_gross_runs_has_no_net_r()
    {
        var result = Create([Run("r1")], [Trade("r1", "A", 1)], new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = Confirmed });

        result.Error.Code.ShouldBe("reports.net_unavailable");
    }

    [Fact]
    public void A_costed_run_whose_trade_has_no_net_r_is_refused()
    {
        Create([Run("r1", costed: true)], [Trade("r1", "A", 1)]).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void A_trade_of_an_unknown_run_is_refused()
    {
        var result = Create([Run("r1")], [Trade("r9", "A", 1)]);

        result.Error.Code.ShouldBe("reports.not_found");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_trade_with_an_r_that_is_not_a_number_is_refused(double r)
    {
        Create([Run("r1")], [Trade("r1", "A", r)]).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void The_same_signal_under_two_exits_is_allowed_but_every_grouping_must_split_by_the_exit()
    {
        var population = Create(
            [Run("r1", exit: "FIXED_RR"), Run("r2", exit: "TRAIL")],
            [.. Series("r1", "ORB", WinsAndLosses(2, 1)), .. Series("r2", "ORB", WinsAndLosses(1, 2))]).Value;

        population.ExitVaries.ShouldBeTrue();
        population.StyleVaries.ShouldBeFalse();
        var unsplit = population.GroupBy([ReportDimension.Strategy]);
        unsplit.Error.Code.ShouldBe("reports.double_count");
        unsplit.Error.Message.ShouldContain("Exit");
        population.GroupBy([]).Error.Code.ShouldBe("reports.double_count");
        var split = Group(population, ReportDimension.Strategy, ReportDimension.Exit);
        split.Groups.Select(g => g.N).ShouldBe([3, 3]);
        split.Groups.Sum(g => g.N).ShouldBe(6);
    }

    [Fact]
    public void Each_exit_is_shrunk_toward_its_own_exit_pool_so_one_exit_does_not_move_the_other()
    {
        var withBad = Population(
            [Run("r1", exit: "A"), Run("r2", exit: "B")],
            [.. Series("r1", "ORB", WinsAndLosses(6, 4)), .. Series("r2", "ORB", Repeat(-1, 10))],
            Small(3, strength: 20));
        var alone = Population([Run("r1", exit: "A")], Series("r1", "ORB", WinsAndLosses(6, 4)), Small(3, strength: 20));

        var a = Only(Group(withBad, ReportDimension.Exit), "Exit=A");
        var b = Only(Group(alone, ReportDimension.Exit), "Exit=A");

        a.ShrunkWinRate.ShouldBe(b.ShrunkWinRate);
        a.PriorRate.ShouldBe(b.PriorRate);
    }

    [Fact]
    public void The_same_signal_under_two_styles_must_be_split_by_style()
    {
        var population = Population(
            [Run("r1", style: "intraday"), Run("r2", style: "swing")],
            [.. Series("r1", "ORB", WinsAndLosses(2, 1)), .. Series("r2", "ORB", WinsAndLosses(1, 2))]);

        population.StyleVaries.ShouldBeTrue();
        population.GroupBy([ReportDimension.Exit]).Error.Code.ShouldBe("reports.double_count");
        Group(population, ReportDimension.Style).Groups.Count.ShouldBe(2);
    }

    [Fact]
    public void The_same_signal_in_two_runs_with_the_same_labels_is_refused_even_when_the_runs_have_other_windows()
    {
        var result = Create(
            [Run("r1", universe: "U1", startOffset: 0, endOffset: 50), Run("r2", universe: "U2", startOffset: 60, endOffset: 120)],
            [Trade("r1", "SAME", 1), Trade("r2", "SAME", 1)]);

        result.Error.Code.ShouldBe("reports.run_overlap");
    }

    [Fact]
    public void The_refusal_does_not_depend_on_the_order_of_the_input()
    {
        var runs = new[] { Run("r1"), Run("r2"), Run("r3", universe: "other") };
        ReportTrade[] trades = [.. Series("r1", "ORB", Repeat(1, 3)), .. Series("r2", "ORB", Repeat(1, 3)), .. Series("r3", "VWAP", Repeat(1, 3))];

        var forward = Create(runs, trades).Error;
        var backward = Create([.. runs.Reverse()], [.. trades.Reverse()]).Error;

        backward.ShouldBe(forward);
    }

    [Fact]
    public void Dimensions_with_no_data_are_refused_with_the_reason_and_unavailable_inputs_are_listed()
    {
        var population = Population([Run("r1", style: null)], Series("r1", "ORB", Repeat(1, 3)));

        var regime = population.GroupBy([ReportDimension.Regime]);
        var account = population.GroupBy([ReportDimension.Account]);

        regime.Error.Code.ShouldBe("reports.dimension_unavailable");
        regime.Error.Message.ShouldContain("no market context");
        account.Error.Message.ShouldContain("no account");
        population.Evidence.UnavailableInputs.ShouldContain("style not recorded for run r1");
        population.Evidence.UnavailableInputs.ShouldContain("basket membership not recorded for 3 trades");
    }

    [Fact]
    public void The_evidence_lists_every_run_of_the_set_the_dates_the_basis_and_the_limits()
    {
        var population = Population([Run("r2"), Run("r1", universe: "V", style: "swing")], [.. Series("r1", "ORB", Repeat(1, 2)), .. Series("r2", "VWAP", Repeat(1, 3), firstDay: 5)]);

        var evidence = population.Evidence;

        evidence.Population.RunKeys.ShouldBe(["r1", "r2"]);
        evidence.Population.RunCount.ShouldBe(2);
        evidence.Population.N.ShouldBe(5);
        evidence.Population.FirstDate.ShouldBe(Day0);
        evidence.Population.LastDate.ShouldBe(Day0.AddDays(7));
        evidence.Limits.ShouldContain(l => l.Contains("no hold-out", StringComparison.Ordinal));
        evidence.Limits.ShouldContain(l => l.Contains("not additive", StringComparison.Ordinal));
    }

    [Fact]
    public void Unusable_options_are_refused()
    {
        Create([], [], new ReportOptions { InsightMinTrades = 0 }).Error.Code.ShouldBe("reports.invalid");
    }
}
