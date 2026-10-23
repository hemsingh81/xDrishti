using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>Smart rules by hand (spec P7-06).</summary>
public sealed class SmartRulesTests
{
    private static readonly DateOnly Day = Candidates.Decision;
    private static readonly DateOnly Before = Day.AddDays(-1);
    private static readonly SmartRulesOptions Opt = new();

    private static IReadOnlyList<RankedCandidate> Ranked(params (string Symbol, double Lower, string Side)[] items)
    {
        var c = items.Select((x, i) => Candidates.Good(x.Symbol, "S" + (i + 1), x.Side, stats: Candidates.Stats("S" + (i + 1), lower: x.Lower)));
        return CandidateSelector.Select(c, new SelectionRules()).Ranked;
    }

    private static IReadOnlyList<RankedCandidate> Four() =>
        Ranked(("AAA", 0.50, "BUY"), ("BBB", 0.48, "BUY"), ("CCC", 0.46, "BUY"), ("DDD", 0.44, "BUY"));

    private static MarketGateInput Gate(double? vix = 15, double? breadth = 0.5, bool? evt = false, DateOnly? asOf = null) => new(asOf ?? Before, vix, breadth, evt);

    private static SmartInputs Full(MarketGateInput? gate = null, CorrelationTable? table = null, IReadOnlyList<OpenPosition>? open = null, IReadOnlyList<TakenTrade>? trades = null) =>
        new(gate ?? Gate(), trades ?? Trades(20, 1), table ?? new CorrelationTable([]), open);

    private static List<TakenTrade> Trades(int n, double r) => [.. Enumerable.Range(0, n).Select(i => new TakenTrade("T" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture), Before.AddDays(-n + i), r))];

    [Theory]
    [InlineData(15, 0.5, false, GateMode.Normal)]
    [InlineData(17.99, 0.5, false, GateMode.Normal)]
    [InlineData(18, 0.5, false, GateMode.Reduced)]
    [InlineData(21.99, 0.5, false, GateMode.Reduced)]
    [InlineData(22, 0.5, false, GateMode.NoTrade)]
    [InlineData(15, 0.29, false, GateMode.Reduced)]
    [InlineData(15, 0.30, false, GateMode.Normal)]
    [InlineData(15, 0.5, true, GateMode.Reduced)]
    [InlineData(25, 0.1, true, GateMode.NoTrade)]
    public void Gate_bands(double vix, double breadth, bool evt, GateMode expected) =>
        MarketGate.Evaluate(Gate(vix, breadth, evt), Day, Opt).Mode.ShouldBe(expected);

    [Fact]
    public void Gate_with_no_input_is_unavailable_never_normal_and_the_plan_says_so()
    {
        var g = MarketGate.Evaluate(null, Day, Opt);
        g.Mode.ShouldBe(GateMode.Unavailable);
        g.Code.ShouldBe(SmartRuleCodes.GateUnavailable);

        var plan = SmartRulesEngine.Apply(Four(), Day, Full() with { Gate = null }, Opt);
        plan.Primary.Count.ShouldBe(4);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.GateUnavailable && f.Text.Contains("market gate unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_missing_one_figure_is_unavailable_but_a_tripped_limit_still_counts()
    {
        MarketGate.Evaluate(Gate(breadth: null), Day, Opt).Mode.ShouldBe(GateMode.Unavailable);
        MarketGate.Evaluate(Gate(evt: null), Day, Opt).Mode.ShouldBe(GateMode.Unavailable);
        MarketGate.Evaluate(Gate(vix: 23, breadth: null), Day, Opt).Mode.ShouldBe(GateMode.NoTrade);
        var partial = SmartRulesEngine.Apply(Four(), Day, Full(Gate(vix: 19, breadth: null)), Opt);
        partial.Gate.Mode.ShouldBe(GateMode.Reduced);
        partial.Flags.ShouldContain(f => f.Text.Contains("partly unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_input_not_before_the_decision_date_is_not_known()
    {
        MarketGate.Evaluate(Gate(vix: 30, asOf: Day), Day, Opt).Mode.ShouldBe(GateMode.Unavailable);
        MarketGate.Evaluate(Gate(double.NaN, double.NaN, null), Day, Opt).Mode.ShouldBe(GateMode.Unavailable);
    }

    [Fact]
    public void Reduced_keeps_half_the_items_and_cuts_risk_to_075()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Full(Gate(vix: 19)), Opt);

        plan.Primary.Select(p => p.Merged.Winner.Candidate.Symbol).ShouldBe(["AAA", "BBB"]);
        plan.Reserve.Select(r => r.Code).ShouldAllBe(c => c == SmartRuleCodes.ReserveGateReduced);
        plan.Reserve.Count.ShouldBe(2);
        plan.RiskMultiplier.ShouldBe(0.75);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf); // 2 of 4 is exactly half: "half or more"

        var five = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.48, "BUY"), ("CCC", 0.46, "BUY"), ("DDD", 0.44, "BUY"), ("EEE", 0.42, "BUY")), Day, Full(Gate(vix: 19)), Opt);
        five.Primary.Count.ShouldBe(3);
        five.Flags.ShouldNotContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf); // 2 of 5
    }

    [Fact]
    public void No_trade_sends_everything_to_reserve_and_flags_the_rule()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Full(Gate(vix: 23)), Opt);

        plan.Primary.ShouldBeEmpty();
        plan.Reserve.Count.ShouldBe(4);
        plan.Reserve.ShouldAllBe(r => r.Code == SmartRuleCodes.ReserveGateNoTrade);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf && f.Text.Contains("market_gate", StringComparison.Ordinal));
    }

    [Fact]
    public void Throttle_bands()
    {
        // 20 trades of +1 R: latest equity 20, average of the points 1..20 is 10.5: clear.
        EquityThrottle.Evaluate(Trades(20, 1), Day, Opt).RiskMultiplier.ShouldBe(1);
        EquityThrottle.Evaluate(Trades(20, 1), Day, Opt).Code.ShouldBe(SmartRuleCodes.ThrottleClear);

        // 20 trades of -1 R: latest -20, average -10.5: below, cut.
        var cut = EquityThrottle.Evaluate(Trades(20, -1), Day, Opt);
        cut.Code.ShouldBe(SmartRuleCodes.ThrottleCut);
        cut.RiskMultiplier.ShouldBe(0.5);
        cut.Equity.ShouldBe(-20);
        cut.Average.ShouldNotBeNull().ShouldBe(-10.5, 1e-9);

        // Flat: equal to the average is not below.
        EquityThrottle.Evaluate(Trades(20, 0), Day, Opt).Code.ShouldBe(SmartRuleCodes.ThrottleClear);
    }

    [Fact]
    public void Throttle_without_enough_real_trades_is_inactive_and_says_why_and_never_cuts_tickets()
    {
        EquityThrottle.Evaluate(null, Day, Opt).Status.ShouldBe(RuleStatus.Unavailable);
        var few = EquityThrottle.Evaluate(Trades(19, -1), Day, Opt);
        few.Code.ShouldBe(SmartRuleCodes.ThrottleUnavailable);
        few.RiskMultiplier.ShouldBe(1);
        few.Reason.ShouldContain("19");

        var plan = SmartRulesEngine.Apply(Four(), Day, Full(trades: []), Opt);
        plan.Primary.Count.ShouldBe(4);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.ThrottleUnavailable);

        var cutPlan = SmartRulesEngine.Apply(Four(), Day, Full(trades: Trades(20, -1)), Opt);
        cutPlan.Primary.Count.ShouldBe(4);
        cutPlan.RiskMultiplier.ShouldBe(0.5);
    }

    [Fact]
    public void Throttle_ignores_trades_closed_on_or_after_the_decision_date()
    {
        var trades = Trades(20, 1);
        trades.Add(new TakenTrade("TX", Day, -500));
        EquityThrottle.Evaluate(trades, Day, Opt).Code.ShouldBe(SmartRuleCodes.ThrottleClear);
        EquityThrottle.Evaluate(trades.Take(19).Append(new TakenTrade("TY", Day, 1)), Day, Opt).Status.ShouldBe(RuleStatus.Unavailable);
    }

    [Fact]
    public void Gate_and_throttle_multiply()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Full(Gate(vix: 19), trades: Trades(20, -1)), Opt);
        plan.RiskMultiplier.ShouldBe(0.375, 1e-9);
    }

    [Fact]
    public void Same_bet_with_a_better_ticket_goes_to_reserve_with_the_numbers()
    {
        var table = new CorrelationTable([("AAA", "BBB", 0.8), ("AAA", "CCC", 0.3), ("BBB", "CCC", 0.2)]);
        var plan = SmartRulesEngine.Apply(Four().Take(3).ToList(), Day, Full(table: table), Opt);

        plan.Primary.Select(p => p.Merged.Winner.Candidate.Symbol).ShouldBe(["AAA", "CCC"]);
        var r = plan.Reserve.ShouldHaveSingleItem();
        r.Code.ShouldBe(SmartRuleCodes.SameBetBetterTicket);
        r.Ranked.Merged.Winner.Candidate.Symbol.ShouldBe("BBB");
        r.Reason.ShouldContain("0.8");
        r.Reason.ShouldContain("AAA");
        plan.CorrelationUnavailablePairs.ShouldBe(0);
    }

    [Fact]
    public void Correlation_at_the_limit_is_not_the_same_bet_and_opposite_sides_are_a_hedge()
    {
        var atLimit = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "BUY")), Day, Full(table: new CorrelationTable([("AAA", "BBB", 0.60)])), Opt);
        atLimit.Primary.Count.ShouldBe(2);

        var hedge = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "SELL")), Day, Full(table: new CorrelationTable([("AAA", "BBB", 0.9)])), Opt);
        hedge.Primary.Count.ShouldBe(2);

        var sameBetShortVsLong = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "SELL")), Day, Full(table: new CorrelationTable([("AAA", "BBB", -0.9)])), Opt);
        sameBetShortVsLong.Reserve.ShouldHaveSingleItem();
    }

    [Fact]
    public void Same_bet_with_an_open_trade_or_the_same_underlying_goes_to_reserve()
    {
        var table = new CorrelationTable([("AAA", "OPEN", 0.7)], new Dictionary<string, string> { ["BBB"] = "U1", ["CCC"] = "U1" });
        var plan = SmartRulesEngine.Apply(Four().Take(3).ToList(), Day, Full(table: table, open: [new OpenPosition("OPEN", "BUY")]), Opt);

        plan.Reserve.Select(r => (r.Ranked.Merged.Winner.Candidate.Symbol, r.Code)).ShouldBe([("AAA", SmartRuleCodes.SameBetOpenTrade), ("CCC", SmartRuleCodes.SameBetBetterTicket)]);
        plan.Reserve[1].Reason.ShouldContain("underlying");
        plan.Primary.Select(p => p.Merged.Winner.Candidate.Symbol).ShouldBe(["BBB"]);
    }

    [Fact]
    public void Without_correlation_data_the_rule_is_unavailable_and_flagged_not_a_pass()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Full() with { Correlations = null }, Opt);

        plan.Correlation.ShouldBe(RuleStatus.Unavailable);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.CorrelationUnavailable);
        plan.Primary.Count.ShouldBe(4);
    }

    [Fact]
    public void Pairs_without_a_figure_are_counted_and_flagged()
    {
        var plan = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "BUY")), Day, Full(), Opt);

        plan.Correlation.ShouldBe(RuleStatus.Applied);
        plan.CorrelationUnavailablePairs.ShouldBe(1);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.CorrelationPartial);
    }

    [Fact]
    public void A_rule_that_removed_half_raises_the_flag_and_fewer_does_not()
    {
        var all = new CorrelationTable([("AAA", "BBB", 0.9), ("AAA", "CCC", 0.9), ("AAA", "DDD", 0.1), ("BBB", "CCC", 0.1), ("BBB", "DDD", 0.1), ("CCC", "DDD", 0.1)]);
        var half = SmartRulesEngine.Apply(Four(), Day, Full(table: all), Opt);
        half.Reserve.Count.ShouldBe(2);
        half.Flags.ShouldContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf && f.Text.Contains("correlation", StringComparison.Ordinal));

        var third = SmartRulesEngine.Apply(Four().Take(3).ToList(), Day, Full(table: new CorrelationTable([("AAA", "BBB", 0.9), ("AAA", "CCC", 0.1), ("BBB", "CCC", 0.1)])), Opt);
        third.Reserve.Count.ShouldBe(1);
        third.Flags.ShouldNotContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf);
    }

    [Fact]
    public void Meta_size_multiplier_is_never_applied()
    {
        SmartRulesEngine.Apply(Four(), Day, Full(), Opt).MetaSizeMultiplierApplied.ShouldBeFalse();
    }

    [Fact]
    public void Switched_off_rules_do_nothing_and_are_reported_off()
    {
        var off = Opt with { GateEnabled = false, ThrottleEnabled = false, CorrelationEnabled = false };
        var plan = SmartRulesEngine.Apply(Four(), Day, new SmartInputs(), off);

        plan.Primary.Count.ShouldBe(4);
        plan.Flags.ShouldBeEmpty();
        plan.Gate.Status.ShouldBe(RuleStatus.Off);
        plan.Throttle.Status.ShouldBe(RuleStatus.Off);
        plan.Correlation.ShouldBe(RuleStatus.Off);
        plan.RiskMultiplier.ShouldBe(1);
    }

    [Fact]
    public void Output_does_not_depend_on_input_order()
    {
        var table = new CorrelationTable([("AAA", "BBB", 0.8), ("CCC", "DDD", 0.9), ("AAA", "CCC", 0.1), ("AAA", "DDD", 0.1), ("BBB", "CCC", 0.1), ("BBB", "DDD", 0.1)]);
        var inputs = Full(Gate(vix: 19), table, trades: Trades(20, -1));
        var baseline = Describe(SmartRulesEngine.Apply(Four(), Day, inputs, Opt));
        var rng = new Random(7);
        for (var i = 0; i < 25; i++)
        {
            var shuffled = Four().OrderBy(_ => rng.Next()).ToList();
            var shuffledTrades = inputs.TakenTrades!.OrderBy(_ => rng.Next()).ToList();
            Describe(SmartRulesEngine.Apply(shuffled, Day, inputs with { TakenTrades = shuffledTrades }, Opt)).ShouldBe(baseline);
        }
    }

    private static string Describe(SmartPlan p) =>
        string.Join(",", p.Primary.Select(x => x.Merged.Winner.Candidate.Symbol)) + "|" +
        string.Join(",", p.Reserve.Select(x => x.Ranked.Merged.Winner.Candidate.Symbol + ":" + x.Code)) + "|" + p.RiskMultiplier + "|" +
        string.Join(",", p.Flags.Select(f => f.Code));

    [Fact]
    public void Correlation_from_returns_uses_the_last_60_sessions_before_the_decision_date()
    {
        var days = Enumerable.Range(1, 70).Select(i => Before.AddDays(-i)).ToList();
        var a = days.Select((d, i) => new DailyReturn(d, Math.Sin(i))).ToList();
        var same = days.Select((d, i) => new DailyReturn(d, 2 * Math.Sin(i))).ToList();
        var inverse = days.Select((d, i) => new DailyReturn(d, -Math.Sin(i))).ToList();
        var future = a.Append(new DailyReturn(Day, 1000)).ToList();
        var short5 = a.Take(5).ToList();
        var table = CorrelationTable.FromReturns(
            new Dictionary<string, IReadOnlyList<DailyReturn>> { ["A"] = future, ["B"] = same, ["C"] = inverse, ["D"] = short5 },
            Day,
            ["A", "B", "C", "d"]);

        table.Get("A", "B").ShouldNotBeNull().ShouldBe(1, 1e-9);
        table.Get("B", "A").ShouldNotBeNull().ShouldBe(1, 1e-9);
        table.Get("A", "C").ShouldNotBeNull().ShouldBe(-1, 1e-9);
        table.Get("A", "D").ShouldBeNull();
    }

    [Fact]
    public void Correlation_from_returns_drops_dates_with_conflicting_duplicates_whatever_the_input_order()
    {
        var days = Enumerable.Range(1, 70).Select(i => Before.AddDays(-i)).ToList();
        var a = days.Select((d, i) => new DailyReturn(d, Math.Sin(i))).ToList();
        var b = days.Select((d, i) => new DailyReturn(d, Math.Sin(i))).ToList();
        var dup = a.Append(new DailyReturn(days[0], 5)).ToList();
        var reversed = Enumerable.Reverse(dup).ToList();

        double? Of(List<DailyReturn> x) => CorrelationTable.FromReturns(new Dictionary<string, IReadOnlyList<DailyReturn>> { ["A"] = x, ["B"] = b }, Day, ["A"], ["B"]).Get("A", "B");

        Of(dup).ShouldBe(Of(reversed));
        Of(dup).ShouldNotBeNull().ShouldBe(1, 1e-9);
    }

    [Fact]
    public void Correlation_from_returns_computes_only_the_pairs_the_plan_needs()
    {
        var days = Enumerable.Range(1, 70).Select(i => Before.AddDays(-i)).ToList();
        IReadOnlyList<DailyReturn> R(double k) => [.. days.Select((d, i) => new DailyReturn(d, Math.Sin(i * k)))];
        var table = CorrelationTable.FromReturns(
            new Dictionary<string, IReadOnlyList<DailyReturn>> { ["A"] = R(1), ["B"] = R(1), ["X"] = R(1), ["Y"] = R(1) }, Day, ["A"], ["b"]);

        table.Get("A", "B").ShouldNotBeNull();
        table.Get("X", "Y").ShouldBeNull();
        table.Get("A", "X").ShouldBeNull();
    }

    [Theory]
    [InlineData("Buy")]
    [InlineData("B")]
    [InlineData("")]
    [InlineData("LONG")]
    public void An_open_position_with_a_side_that_is_not_BUY_or_SELL_is_never_a_hedge_and_is_flagged(string side)
    {
        var table = new CorrelationTable([("AAA", "OPEN", 0.95)]);
        var plan = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY")), Day, Full(table: table, open: [new OpenPosition("OPEN", side)]), Opt);

        plan.CorrelationUnavailablePairs.ShouldBe(1);
        plan.Flags.ShouldContain(f => f.Code == SmartRuleCodes.CorrelationPartial);
        plan.Reserve.ShouldBeEmpty();
    }

    [Fact]
    public void A_candidate_with_an_unknown_side_is_not_screened_and_is_flagged()
    {
        var table = new CorrelationTable([("AAA", "BBB", 0.95)]);
        var plan = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "buy")), Day, Full(table: table), Opt);

        plan.CorrelationUnavailablePairs.ShouldBe(1);
        plan.Primary.Count.ShouldBe(2);
    }

    [Fact]
    public void Stale_gate_figures_read_unavailable()
    {
        MarketGate.Evaluate(Gate(vix: 30, asOf: Day.AddDays(-4)), Day, Opt).Mode.ShouldBe(GateMode.NoTrade);
        MarketGate.Evaluate(Gate(asOf: Day.AddDays(-5)), Day, Opt).Mode.ShouldBe(GateMode.Unavailable);
        MarketGate.Evaluate(Gate(vix: 30, asOf: Day.AddDays(-90)), Day, Opt).Mode.ShouldBe(GateMode.Unavailable);
    }

    [Fact]
    public void Two_symbols_of_one_underlying_on_opposite_sides_are_a_hedge()
    {
        var table = new CorrelationTable([], new Dictionary<string, string> { ["AAA"] = "U", ["BBB"] = "U" });
        var plan = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "SELL")), Day, Full(table: table), Opt);

        plan.Primary.Count.ShouldBe(2);
        plan.CorrelationUnavailablePairs.ShouldBe(0);
    }

    [Fact]
    public void An_open_trade_hit_takes_precedence_over_a_better_ticket_hit()
    {
        var table = new CorrelationTable([("BBB", "AAA", 0.9), ("BBB", "OPEN", 0.7)]);
        var plan = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "BUY")), Day, Full(table: table, open: [new OpenPosition("OPEN", "BUY")]), Opt);

        var r = plan.Reserve.ShouldHaveSingleItem();
        r.Code.ShouldBe(SmartRuleCodes.SameBetOpenTrade);
        r.Reason.ShouldContain("OPEN");
    }

    [Fact]
    public void A_no_trade_gate_still_reports_the_correlation_rule_with_no_pairs_to_check()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Full(Gate(vix: 25), new CorrelationTable([])), Opt);

        plan.Primary.ShouldBeEmpty();
        plan.Correlation.ShouldBe(RuleStatus.Applied);
        plan.CorrelationUnavailablePairs.ShouldBe(0);
    }

    [Fact]
    public void Honesty_boundary_exactly_half_flags_and_just_under_does_not()
    {
        // 1 of 2 removed is exactly half: flagged. 1 of 3 is not.
        var two = SmartRulesEngine.Apply(Ranked(("AAA", 0.5, "BUY"), ("BBB", 0.4, "BUY")), Day, Full(table: new CorrelationTable([("AAA", "BBB", 0.9)])), Opt);
        two.Flags.ShouldContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf);
        var three = SmartRulesEngine.Apply(Four().Take(3).ToList(), Day, Full(table: new CorrelationTable([("AAA", "BBB", 0.9), ("AAA", "CCC", 0.1), ("BBB", "CCC", 0.1)])), Opt);
        three.Flags.ShouldNotContain(f => f.Code == SmartRuleCodes.RuleRemovedHalf);
    }

    [Fact]
    public void Invalid_options_are_listed_and_refused()
    {
        new SmartRulesOptions().Problems().ShouldBeEmpty();
        (Opt with { VixReduce = 23 }).Problems().ShouldNotBeEmpty();
        (Opt with { ReducedItemsFactor = 0 }).Problems().ShouldNotBeEmpty();
        (Opt with { ReducedItemsFactor = 1.5 }).Problems().ShouldNotBeEmpty();
        (Opt with { ThrottleCut = 0 }).Problems().ShouldNotBeEmpty();
        (Opt with { HonestyFraction = double.NaN }).Problems().ShouldNotBeEmpty();
        (Opt with { GateMaxAgeDays = 0 }).Problems().ShouldNotBeEmpty();
        Should.Throw<ArgumentException>(() => SmartRulesEngine.Apply(Four(), Day, new SmartInputs(), Opt with { ThrottleCut = 2 }));
    }
}
