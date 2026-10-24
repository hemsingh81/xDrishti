using System.Reflection;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>
/// P8-06 end to end behind the ports: the figures of a small world worked by hand (two runs, three baskets), the population rule's refusals with their codes, the six honest states (a non-value never carries a number), the evidence block on every answer, the paging
/// of the trade list and the rule that a net report exists only by the owner's stored confirmation.
/// </summary>
public sealed class ReportHandlersTests
{
    private const string B1 = "11111111-1111-1111-1111-111111111111";
    private const string B2 = "22222222-2222-2222-2222-222222222222";
    private const string B3 = "33333333-3333-3333-3333-333333333333";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<double> Mix(int wins, double win, int losses, double loss) => Enumerable.Repeat(win, wins).Concat(Enumerable.Repeat(loss, losses));

    /// <summary>
    /// Run a: ORB over basket 1, 40 trades, 25 of +1 R then 15 of -1 R (total +10, expectancy 0.25). Run b: ORB over basket 2, 30 trades, 12 of +2 then 18 of -1 (total +6, expectancy 0.2).
    /// Run c: VWAP over basket 3, 35 trades, 14 of +1 then 21 of -0.5 (total +3.5, expectancy 0.1). The baskets share no signal.
    /// </summary>
    private static ReportWorld ThreeBaskets()
    {
        var world = new ReportWorld();
        world.AddRun('a', "ORB", "basket:" + B1);
        world.AddTrades('a', Mix(25, 1, 15, -1), "ORB", baskets: [B1]);
        world.AddRun('b', "ORB", "basket:" + B2, createdDays: 1);
        world.AddTrades('b', Mix(12, 2, 18, -1), "ORB", baskets: [B2], symbol: "BBB");
        world.AddRun('c', "VWAP", "basket:" + B3, createdDays: 2);
        world.AddTrades('c', Mix(14, 1, 21, -0.5), "VWAP", baskets: [B3], symbol: "CCC");
        return world;
    }

    private static ReportQuery Q(ReportQueryInput? input = null) => ReportWorld.Query(input);

    private static async Task<T> Ok<T>(ValueTask<Result<T>> call)
    {
        var result = await call;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    private static async Task<Error> Refused<T>(ValueTask<Result<T>> call)
    {
        var result = await call;
        result.IsFailure.ShouldBeTrue();
        return result.Error;
    }

    [Fact]
    public async Task The_summary_of_one_run_is_the_hand_worked_figures_with_the_evidence_of_the_run()
    {
        var world = ThreeBaskets();
        var handler = new GetReportSummaryHandler(world.Loader);

        var view = await Ok(handler.HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('a')))), Ct));

        view.All.N.ShouldBe(40);
        view.All.Wins.ShouldBe(25);
        view.All.WinRate.State.ShouldBe("value");
        view.All.WinRate.Number!.Value.ShouldBe(0.625, 1e-12);
        view.All.ExpectancyR.Number!.Value.ShouldBe(0.25, 1e-12);
        view.All.TotalR.Number!.Value.ShouldBe(10, 1e-12);
        view.All.ProfitFactor.Number!.Value.ShouldBe(25.0 / 15, 1e-12);
        view.All.AvgWinR.Number!.Value.ShouldBe(1, 1e-12);
        view.All.AvgLossR.Number!.Value.ShouldBe(-1, 1e-12);
        view.All.MaxDrawdownR.Number!.Value.ShouldBe(15, 1e-12);
        view.All.LongestLossStreak.Number!.Value.ShouldBe(15, 1e-12);
        view.All.AvgHoldingSessions.Number!.Value.ShouldBe(1, 1e-12);
        view.Evidence.Basis.ShouldBe("gross");
        view.Evidence.N.ShouldBe(40);
        view.Evidence.RunKeys.ShouldBe([ReportWorld.Key('a')]);
        view.Evidence.Runs.Single().StrategyId.ShouldBe("ORB");
        view.Evidence.DefaultRunSet.ShouldBeFalse();
        view.Evidence.OpenAtEndLeftOut.ShouldBe(2);
        view.Evidence.Limits.ShouldContain(l => l.Contains("Replayed history", StringComparison.Ordinal));
        view.Evidence.Limits.ShouldContain(l => l.Contains("Only closed trades", StringComparison.Ordinal));
        view.Evidence.Limits.ShouldContain(l => l.Contains("2 trades still open", StringComparison.Ordinal));
        view.Evidence.UnavailableInputs.ShouldContain(i => i.Contains("Regime", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Grouped_by_basket_the_three_baskets_have_their_hand_worked_totals_and_the_default_run_set_is_the_three_runs()
    {
        var world = ThreeBaskets();
        var handler = new GetReportGroupsHandler(world.Loader);

        var view = await Ok(handler.HandleAsync(new GetReportGroupsQuery(Q(new ReportQueryInput(Lens: "basket"))), Ct));

        view.Dimensions.ShouldBe(["basket"]);
        view.Groups.Select(g => (g.Key, g.N)).ShouldBe([($"Basket={B1}", 40), ($"Basket={B2}", 30), ($"Basket={B3}", 35)]);
        view.Groups[0].Parts.ShouldBe([new GroupPartView("basket", B1)]);
        view.Groups.Select(g => g.TotalR.Number!.Value).ShouldBe([10.0, 6.0, 3.5]);
        view.Groups.Select(g => g.ExpectancyR.Number!.Value).ShouldBe([0.25, 0.2, 0.1], tolerance: 1e-12);
        view.Evidence.N.ShouldBe(105);
        view.Evidence.DefaultRunSet.ShouldBeTrue();
        view.Evidence.RunKeys.Count.ShouldBe(3);
        view.NonAdditive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_pivot_of_basket_by_strategy_has_every_cell_and_leaves_the_gaps_empty_never_zero()
    {
        var world = ThreeBaskets();
        var handler = new GetReportPivotHandler(world.Loader);

        var view = await Ok(handler.HandleAsync(new GetReportPivotQuery(Q(new ReportQueryInput(Lens: "basket,strategy", Metric: "total_r"))), Ct));

        view.Metric.ShouldBe("total_r");
        view.Rows.ShouldBe([B1, B2, B3]);
        view.Columns.ShouldBe(["ORB", "VWAP"]);
        view.Cells.Count.ShouldBe(6);
        var cell = (string row, string column) => view.Cells.Single(c => c.Row == row && c.Column == column);
        cell(B1, "ORB").Value.Number!.Value.ShouldBe(10);
        cell(B2, "ORB").Value.Number!.Value.ShouldBe(6);
        cell(B3, "VWAP").Value.Number!.Value.ShouldBe(3.5);
        foreach (var gap in new[] { cell(B1, "VWAP"), cell(B2, "VWAP"), cell(B3, "ORB") })
        {
            gap.N.ShouldBe(0);
            gap.Value.State.ShouldBe("empty");
            gap.Value.Number.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("strategy")]
    [InlineData("basket,strategy,side")]
    public async Task A_pivot_needs_a_lens_of_exactly_two_dimensions_and_reads_nothing_otherwise(string lens)
    {
        var world = ThreeBaskets();
        var handler = new GetReportPivotHandler(world.Loader);

        var error = await Refused(handler.HandleAsync(new GetReportPivotQuery(Q(new ReportQueryInput(Lens: lens))), Ct));

        error.Code.ShouldBe("reports.invalid");
        world.TradeStreams.ShouldBe(0);
    }

    [Fact]
    public async Task The_equity_curve_of_a_run_ends_at_its_total_and_a_thin_group_has_no_curve()
    {
        var world = ThreeBaskets();
        world.AddRun('d', "THIN", "basket:" + B3, createdDays: 3);
        world.AddTrades('d', [1, -1, 2], "THIN", baskets: [B3], symbol: "DDD");
        var handler = new GetReportEquityHandler(world.Loader);

        var view = await Ok(handler.HandleAsync(new GetReportEquityQuery(Q(new ReportQueryInput(Lens: "strategy"))), Ct));

        view.Series.Select(s => s.Key).ShouldBe(["Strategy=ORB", "Strategy=THIN", "Strategy=VWAP"]);
        var orb = view.Series[0];
        orb.N.ShouldBe(70);
        orb.Points[^1].CumulativeR.ShouldBe(16, 1e-12);
        orb.Points.Sum(p => p.Trades).ShouldBe(70);
        var thin = view.Series[1];
        thin.TotalR.State.ShouldBe("insufficient");
        thin.TotalR.Number.ShouldBeNull();
        thin.Points.ShouldBeEmpty();
    }

    [Fact]
    public async Task More_curves_than_the_limit_are_refused_not_cut()
    {
        var world = new ReportWorld();
        world.AddRun('a', "ORB", "basket:" + B1);
        for (var i = 0; i < EquityView.MaxSeries + 1; i++)
        {
            world.AddTrades('a', [1.0], "ORB", symbol: "S" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), sameKeys: false, firstDay: i);
        }

        var error = await Refused(new GetReportEquityHandler(world.Loader).HandleAsync(new GetReportEquityQuery(Q(new ReportQueryInput(Lens: "instrument"))), Ct));

        error.Code.ShouldBe("reports.invalid");
        error.Message.ShouldContain("at most 40");
    }

    /// <summary>One run of one trade for each of 2,001 symbols on consecutive days: more groups than any answer may hold.</summary>
    private static ReportWorld ManySymbols()
    {
        var world = new ReportWorld();
        world.AddRun('a', "ORB", "basket:" + B1);
        for (var i = 0; i < ResponseCaps.Groups + 1; i++)
        {
            world.AddTrades('a', [1.0], symbol: "S" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), firstDay: i);
        }

        return world;
    }

    [Fact]
    public async Task An_answer_over_its_cap_is_refused_with_the_count_never_cut()
    {
        var world = ManySymbols();
        var instruments = Q(new ReportQueryInput(Lens: "instrument"));
        var cells = Q(new ReportQueryInput(Lens: "instrument,weekday"));

        var groups = await Refused(new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(instruments), Ct));
        var pivot = await Refused(new GetReportPivotHandler(world.Loader).HandleAsync(new GetReportPivotQuery(cells), Ct));
        var distribution = await Refused(new GetReportDistributionHandler(world.Loader).HandleAsync(new GetReportDistributionQuery(instruments), Ct));
        var works = await Refused(new GetReportWhatWorksHandler(world.Loader).HandleAsync(new GetReportWhatWorksQuery(instruments), Ct));

        groups.Code.ShouldBe("reports.invalid");
        groups.Message.ShouldContain("2001 groups");
        groups.Message.ShouldContain("at most 2000");
        pivot.Message.ShouldContain("cells");
        distribution.Message.ShouldContain("at most 200");
        works.Message.ShouldContain("at most 2000");
    }

    [Fact]
    public async Task The_distribution_of_a_run_counts_every_trade_once()
    {
        var world = ThreeBaskets();

        var view = await Ok(new GetReportDistributionHandler(world.Loader).HandleAsync(new GetReportDistributionQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('b')))), Ct));

        view.BinWidthR.ShouldBe(0.5);
        var group = view.Groups.Single();
        group.N.ShouldBe(30);
        group.Bins.Sum(b => b.Count).ShouldBe(30);
        group.Bins.Single(b => b.LowerR == 2).Count.ShouldBe(12);
        group.Bins.Single(b => b.LowerR == -1).Count.ShouldBe(18);
        group.MeanR.Number!.Value.ShouldBe(0.2, 1e-12);
    }

    [Fact]
    public async Task What_works_answers_nothing_has_enough_evidence_as_a_200_when_no_cell_qualifies()
    {
        var world = ThreeBaskets();

        var view = await Ok(new GetReportWhatWorksHandler(world.Loader).HandleAsync(new GetReportWhatWorksQuery(Q()), Ct));

        view.Kind.ShouldBe("nothing_qualifies");
        view.Headline.ShouldContain("enough evidence", Shouldly.Case.Insensitive);
        view.Ranked.ShouldBeEmpty();
        view.CellsLookedAt.ShouldBeGreaterThan(0);
        view.NotRanked.ShouldAllBe(c => c.Reason.Contains("needed", StringComparison.Ordinal));
        view.RequiredTrades.ShouldBeGreaterThan(view.MinTrades);
        view.Evidence.N.ShouldBe(105);
        view.UnavailableDimensions.ShouldBeEmpty();
        view.Dimensions.ShouldBe(["basket", "strategy", "timeframe", "side", "exit"]);
    }

    [Fact]
    public async Task The_insights_are_the_eight_in_a_fixed_order_and_a_thin_population_states_no_figure()
    {
        var world = new ReportWorld();
        world.AddRun('a');
        world.AddTrades('a', [1.0, -1, 2]);

        var view = await Ok(new GetReportInsightsHandler(world.Loader).HandleAsync(new GetReportInsightsQuery(Q()), Ct));

        view.Insights.Select(i => i.Kind).ShouldBe(["best_strategy", "weakest_strategy", "basket_gap", "side_bias", "best_exit", "weekday", "regime", "calibration"]);
        view.Insights.ShouldNotContain(i => i.State == "value");
        view.Evidence.N.ShouldBe(3);
    }

    [Fact]
    public async Task The_trade_list_is_paged_in_exit_order_with_a_cursor_and_ends_with_none()
    {
        var world = ThreeBaskets();
        var handler = new PageReportTradesHandler(world.Loader);
        var first = await Ok(handler.HandleAsync(new PageReportTradesQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('a'), Limit: "15"))), Ct));
        var second = await Ok(handler.HandleAsync(new PageReportTradesQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('a'), Limit: "15", Cursor: first.NextCursor))), Ct));
        var third = await Ok(handler.HandleAsync(new PageReportTradesQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('a'), Limit: "15", Cursor: second.NextCursor))), Ct));

        first.Total.ShouldBe(40);
        first.Items.Count.ShouldBe(15);
        first.NextCursor.ShouldBe("o15");
        second.Items.Count.ShouldBe(15);
        second.NextCursor.ShouldBe("o30");
        third.Items.Count.ShouldBe(10);
        third.NextCursor.ShouldBeNull();
        first.Items.Concat(second.Items).Concat(third.Items).Select(t => t.TradeKey).Distinct().Count().ShouldBe(40);
        first.Items.Concat(second.Items).Concat(third.Items).Select(t => t.ExitDate).ShouldBe(first.Items.Concat(second.Items).Concat(third.Items).Select(t => t.ExitDate).Order());
        first.Evidence.N.ShouldBe(40);
        first.Items[0].BasketIds.ShouldBe([B1]);
    }

    [Fact]
    public async Task Filters_narrow_the_trades_and_a_style_filter_narrows_the_runs()
    {
        var world = ThreeBaskets();
        world.Runs[2] = world.Runs[2] with { Style = "swing" };

        var bySymbol = await Ok(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Symbol: "BBB"))), Ct));
        var bySwing = await Ok(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Style: "swing"))), Ct));

        bySymbol.All.N.ShouldBe(30);
        bySwing.All.N.ShouldBeGreaterThan(0);
        bySwing.Evidence.RunKeys.Count.ShouldBe(1);
        bySwing.Evidence.Runs.Single().Style.ShouldBe("swing");
    }

    [Fact]
    public async Task With_no_stored_run_the_summary_is_empty_states_without_a_digit_and_nothing_is_read()
    {
        var world = new ReportWorld();

        var view = await Ok(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q()), Ct));

        view.All.N.ShouldBe(0);
        MetricsOf(view.All).ShouldAllBe(m => m.State == "empty" && m.Number == null && m.Reason == "no trades");
        view.Evidence.N.ShouldBe(0);
        view.Evidence.Limits.ShouldNotBeEmpty();
        view.Evidence.DefaultRunSet.ShouldBeTrue();
        world.TradeStreams.ShouldBe(0);
    }

    [Fact]
    public async Task A_thin_group_is_insufficient_with_no_number_and_the_property_holds_for_every_figure_of_every_group()
    {
        var world = ThreeBaskets();
        world.AddRun('d', "THIN", "basket:" + B3, createdDays: 3);
        world.AddTrades('d', [1, -1, 2], "THIN", baskets: [B3], symbol: "DDD");

        var view = await Ok(new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(Q(new ReportQueryInput(Lens: "strategy,side"))), Ct));

        var thin = view.Groups.Single(g => g.Key.StartsWith("Strategy=THIN", StringComparison.Ordinal));
        thin.N.ShouldBe(3);
        MetricsOf(thin).ShouldAllBe(m => m.State == "insufficient" && m.Number == null && m.Reason!.Contains("at least 30", StringComparison.Ordinal));
        foreach (var metric in view.Groups.SelectMany(MetricsOf))
        {
            (metric.Number is not null).ShouldBe(metric.State is "value" or "provisional", metric.State);
            if (metric.Number is { } number)
            {
                double.IsFinite(number).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public async Task A_second_run_of_the_same_strategy_and_universe_over_an_overlapping_window_is_refused_as_a_duplicate()
    {
        var world = ThreeBaskets();
        world.AddRun('d', "ORB", "basket:" + B1, createdDays: 5, startOffset: 100, endOffset: 400);
        world.AddTrades('d', Mix(5, 1, 5, -1), "ORB", baskets: [B1], symbol: "DDD", firstDay: 200);

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('a') + "," + ReportWorld.Key('d')))), Ct));

        error.Code.ShouldBe("reports.run_overlap");
        error.Kind.ShouldBe(ErrorKind.Conflict);
        error.Message.ShouldContain("ORB");
    }

    [Fact]
    public async Task The_same_signal_in_two_runs_is_refused_even_over_different_universes()
    {
        var world = new ReportWorld();
        world.AddRun('a', "ORB", "basket:" + B1);
        world.AddRun('b', "ORB", "basket:" + B2, createdDays: 1);
        world.AddTrades('a', Mix(20, 1, 20, -1), baskets: [B1], sameKeys: true);
        world.AddTrades('b', Mix(20, 1, 20, -1), baskets: [B2], sameKeys: true);

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q()), Ct));

        error.Code.ShouldBe("reports.run_overlap");
    }

    [Fact]
    public async Task Gross_and_costed_runs_in_one_set_are_refused()
    {
        var world = ThreeBaskets();
        world.AddRun('e', "EMA", "basket:" + B1, sheet: "sheet-1", createdDays: 4);
        world.AddTrades('e', Mix(20, 1, 20, -1), "EMA", cost: 0.1, symbol: "EEE");

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Runs: ReportWorld.Key('a') + "," + ReportWorld.Key('e')))), Ct));

        error.Code.ShouldBe("reports.mixed_basis");
        error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found_and_no_trade_is_read()
    {
        var world = ThreeBaskets();

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Runs: new string('f', 64)))), Ct));

        error.Code.ShouldBe("reports.not_found");
        error.Kind.ShouldBe(ErrorKind.NotFound);
        world.TradeStreams.ShouldBe(0);
    }

    [Fact]
    public async Task Regime_and_account_are_refused_with_the_reason()
    {
        var world = ThreeBaskets();

        var regime = await Refused(new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(Q(new ReportQueryInput(Lens: "regime"))), Ct));
        var account = await Refused(new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(Q(new ReportQueryInput(Lens: "account"))), Ct));

        regime.Code.ShouldBe("reports.dimension_unavailable");
        regime.Message.ShouldContain("no market context");
        account.Code.ShouldBe("reports.dimension_unavailable");
    }

    [Fact]
    public async Task A_net_report_is_refused_when_the_owner_has_not_confirmed_the_sheet_and_no_trade_is_read()
    {
        var world = new ReportWorld();
        world.AddRun('n', "ORB", "basket:" + B1, sheet: "sheet-1");
        world.AddTrades('n', Mix(20, 1, 20, -1), cost: 0.1);

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Basis: "net"))), Ct));
        var gross = await Ok(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q()), Ct));

        error.Code.ShouldBe("reports.net_not_confirmed");
        error.Kind.ShouldBe(ErrorKind.Validation);
        gross.Evidence.Basis.ShouldBe("gross");
        gross.All.ExpectancyR.Number!.Value.ShouldBe(0, 1e-12);
        gross.Evidence.NetBasisConfirmation.ShouldBeNull();
    }

    [Fact]
    public async Task A_net_report_is_made_only_from_the_stored_confirmation_and_prints_what_was_confirmed()
    {
        var world = new ReportWorld();
        world.AddRun('n', "ORB", "basket:" + B1, sheet: "sheet-1");
        world.AddTrades('n', Mix(20, 1, 20, -1), cost: 0.1);
        world.Confirmation = new ChargeSheetConfirmation("sheet-1", new DateOnly(2026, 10, 1));

        var view = await Ok(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Basis: "net"))), Ct));

        view.Evidence.Basis.ShouldBe("net");
        view.Evidence.NetBasisConfirmation.ShouldBe("charge sheet 'sheet-1' confirmed by the owner on 2026-10-01");
        view.All.ExpectancyR.Number!.Value.ShouldBe(-0.1, 1e-12);
        view.All.TotalR.Number!.Value.ShouldBe(-4, 1e-9);
        view.Evidence.Limits.ShouldContain(l => l.Contains("net of the stored charge sheet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_net_report_over_a_run_costed_with_another_sheet_than_the_confirmed_one_is_refused()
    {
        var world = new ReportWorld();
        world.AddRun('n', "ORB", "basket:" + B1, sheet: "sheet-2");
        world.AddTrades('n', Mix(20, 1, 20, -1), cost: 0.1);
        world.Confirmation = new ChargeSheetConfirmation("sheet-1", new DateOnly(2026, 10, 1));

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Basis: "net"))), Ct));

        error.Code.ShouldBe("reports.net_unavailable");
        error.Message.ShouldContain("sheet-2");
        error.Message.ShouldContain("sheet-1");
    }

    [Fact]
    public async Task A_net_report_over_a_gross_run_is_refused_even_with_a_confirmation()
    {
        var world = new ReportWorld();
        world.AddRun('g', "ORB", "basket:" + B1);
        world.AddTrades('g', Mix(20, 1, 20, -1));
        world.Confirmation = new ChargeSheetConfirmation("sheet-1", new DateOnly(2026, 10, 1));

        var error = await Refused(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(Q(new ReportQueryInput(Basis: "net"))), Ct));

        error.Code.ShouldBe("reports.net_unavailable");
    }

    [Fact]
    public async Task The_runs_list_shows_the_default_set_the_reason_for_each_run_left_out_and_net_unavailable_with_the_reason()
    {
        var world = ThreeBaskets();
        world.AddRun('d', "ORB", "basket:" + B1, createdDays: 9);
        world.AddTrades('d', [1.0, -1], "ORB", baskets: [B1], symbol: "DDD", firstDay: 400);

        var view = await Ok(new ListReportRunsHandler(world, world).HandleAsync(new ListReportRunsQuery(), Ct));

        view.Runs.Select(r => r.RunKey).First().ShouldBe(ReportWorld.Key('d'));
        view.Runs.Single(r => r.RunKey == ReportWorld.Key('a')).InDefaultSet.ShouldBeFalse();
        view.Runs.Single(r => r.RunKey == ReportWorld.Key('a')).DefaultSetNote.ShouldBe("an older run of the same strategy and universe");
        view.Runs.Where(r => r.InDefaultSet).Select(r => r.RunKey).ShouldBe([ReportWorld.Key('d'), ReportWorld.Key('b'), ReportWorld.Key('c')], ignoreOrder: true);
        view.Net.State.ShouldBe("unavailable");
        view.Net.Reason!.ShouldContain("not confirmed");
        view.Net.Confirmed.ShouldBeNull();
        view.Runs.ShouldAllBe(r => !r.NetPossible);
        view.DefaultSetRule.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task With_a_stored_confirmation_only_the_runs_costed_with_that_sheet_can_be_net()
    {
        var world = new ReportWorld();
        world.AddRun('a', "ORB", sheet: "sheet-1");
        world.AddRun('b', "VWAP", sheet: "sheet-2", createdDays: 1);
        world.AddRun('c', "EMA", createdDays: 2);
        world.Confirmation = new ChargeSheetConfirmation("sheet-1", new DateOnly(2026, 10, 1));

        var view = await Ok(new ListReportRunsHandler(world, world).HandleAsync(new ListReportRunsQuery(), Ct));

        view.Net.State.ShouldBe("value");
        view.Net.Confirmed!.ShouldContain("sheet-1");
        view.Runs.Where(r => r.NetPossible).Select(r => r.RunKey).ShouldBe([ReportWorld.Key('a')]);
    }

    [Fact]
    public async Task With_no_stored_run_the_runs_list_is_empty()
    {
        var view = await Ok(new ListReportRunsHandler(new ReportWorld(), new ReportWorld()).HandleAsync(new ListReportRunsQuery(), Ct));

        view.Runs.ShouldBeEmpty();
        view.Net.State.ShouldBe("unavailable");
    }

    [Fact]
    public async Task Every_answer_carries_the_evidence_block_with_its_limits()
    {
        var world = ThreeBaskets();
        var query = Q(new ReportQueryInput(Lens: "strategy,side"));

        var evidences = new List<ReportEvidenceView>
        {
            (await Ok(new GetReportSummaryHandler(world.Loader).HandleAsync(new GetReportSummaryQuery(query), Ct))).Evidence,
            (await Ok(new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(query), Ct))).Evidence,
            (await Ok(new GetReportPivotHandler(world.Loader).HandleAsync(new GetReportPivotQuery(query), Ct))).Evidence,
            (await Ok(new GetReportEquityHandler(world.Loader).HandleAsync(new GetReportEquityQuery(query), Ct))).Evidence,
            (await Ok(new GetReportDistributionHandler(world.Loader).HandleAsync(new GetReportDistributionQuery(query), Ct))).Evidence,
            (await Ok(new GetReportInsightsHandler(world.Loader).HandleAsync(new GetReportInsightsQuery(query), Ct))).Evidence,
            (await Ok(new GetReportWhatWorksHandler(world.Loader).HandleAsync(new GetReportWhatWorksQuery(Q()), Ct))).Evidence,
            (await Ok(new PageReportTradesHandler(world.Loader).HandleAsync(new PageReportTradesQuery(query), Ct))).Evidence,
        };

        evidences.Count.ShouldBe(8);
        evidences.ShouldAllBe(e => e.N == 105 && e.Basis == "gross" && e.Limits.Count >= 8 && e.RunKeys.Count == 3 && e.Runs.Count == 3 && e.UnavailableInputs.Count > 0);
    }

    [Fact]
    public async Task The_same_runs_give_the_same_answer_whatever_the_order_the_store_returns_them()
    {
        var forward = ThreeBaskets();
        var backward = ThreeBaskets();
        backward.Trades.Reverse();

        var one = await Ok(new GetReportGroupsHandler(forward.Loader).HandleAsync(new GetReportGroupsQuery(Q(new ReportQueryInput(Lens: "basket,strategy"))), Ct));
        var two = await Ok(new GetReportGroupsHandler(backward.Loader).HandleAsync(new GetReportGroupsQuery(Q(new ReportQueryInput(Lens: "basket,strategy"))), Ct));

        two.ShouldBe(one, new GroupsViewComparer());
    }

    private static IEnumerable<MetricView> MetricsOf(GroupView group) =>
        typeof(GroupView).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(MetricView)).Select(p => (MetricView)p.GetValue(group)!);

    /// <summary>The views are records over lists, which compare by reference; the answer's bytes are compared through the JSON of its figures.</summary>
    private sealed class GroupsViewComparer : IEqualityComparer<GroupsView>
    {
        public bool Equals(GroupsView? x, GroupsView? y) => Json(x) == Json(y);

        public int GetHashCode(GroupsView obj) => Json(obj).GetHashCode(StringComparison.Ordinal);

        private static string Json(GroupsView? view) => System.Text.Json.JsonSerializer.Serialize(view);
    }
}
