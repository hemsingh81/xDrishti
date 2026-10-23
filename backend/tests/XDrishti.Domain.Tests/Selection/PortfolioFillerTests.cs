using XDrishti.Domain.Learning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

public sealed class PortfolioFillerTests
{
    private static FillCandidate Fc(
        string symbol,
        int rank,
        string side = "BUY",
        string style = "intraday",
        double? risk = 0.5,
        string? sector = null,
        string[]? baskets = null,
        bool? held = false)
    {
        var plan = Candidates.Good(symbol, side: side) with { Cell = new CellId("S1", "15m", side, style, "fixed") };
        var merged = new MergedCandidate(new Eligibility(plan, EligibilityVerdict.Eligible, [], 0.5), 0, []);
        return new FillCandidate(new RankedCandidate(rank, merged, 1.0 - (rank * 0.01), []), risk, sector ?? "sec-" + symbol, baskets ?? [], held);
    }

    private static BookPosition Held(string symbol, double risk = 0.1, string side = "BUY", string style = "swing", string? sector = null) =>
        new(symbol, side, style, risk, sector ?? "book-" + symbol, []);

    private static readonly string[] Names = ["A", "B", "C", "D", "E", "F", "G", "H", "H1", "H2", "H3", "H4"];

    // Every pair is known (0.1) unless given: the correlation check is unavailable for a missing pair, which is tested on its own.
    private static CorrelationMatrix Corr(params (string, string, double)[] pairs) =>
        new([.. pairs, .. Names.SelectMany(a => Names.Select(b => (a, b, 0.1)))]);

    private static FillResult Run(FillCandidate[] cands, FillConstraints? k = null, BookPosition[]? book = null, CorrelationMatrix? corr = null) =>
        PortfolioFiller.Fill(cands, book ?? [], corr ?? Corr(), k ?? new FillConstraints(MaxPlanItems: 10, MaxIntraday: 9, MaxSwing: 9));

    private static string[] Symbols(IEnumerable<FillDecision> d) => [.. d.Select(x => x.Candidate.Symbol)];

    private static FillCheck Reason(FillDecision d, string code) => d.Reasons.Single(r => r.Code == code);

    [Fact]
    public void Takes_every_candidate_when_no_constraint_binds()
    {
        var r = Run([Fc("A", 1), Fc("B", 2), Fc("C", 3)]);

        Symbols(r.Primary).ShouldBe(["A", "B", "C"]);
        r.Reserve.ShouldBeEmpty();
        r.Rejected.ShouldBeEmpty();
        r.Primary.ShouldAllBe(d => d.Reasons.Count == 0);
    }

    [Fact]
    public void Plan_items_limit_blocks_the_next_pick_in_trigger_mode()
    {
        var r = Run([Fc("A", 1), Fc("B", 2), Fc("C", 3)], new FillConstraints(MaxPlanItems: 2));

        Symbols(r.Primary).ShouldBe(["A", "B"]);
        Symbols(r.Reserve).ShouldBe(["C"]);
        Reason(r.Reserve[0], FillCodes.PlanItems).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void Entries_per_day_blocks_the_next_pick_in_plan_mode_but_not_in_trigger_mode()
    {
        var plan = Run([Fc("A", 1), Fc("B", 2)], new FillConstraints(MaxEntriesPerDay: 1, CountEntriesOn: EntriesCountMode.Plan));
        var trigger = Run([Fc("A", 1), Fc("B", 2)], new FillConstraints(MaxEntriesPerDay: 1, CountEntriesOn: EntriesCountMode.Trigger, MaxPlanItems: 5));

        Symbols(plan.Primary).ShouldBe(["A"]);
        Reason(plan.Reserve.Single(), FillCodes.EntriesPerDay).Value.ShouldBe(2);
        Symbols(trigger.Primary).ShouldBe(["A", "B"]);
    }

    [Fact]
    public void Open_positions_count_the_book_and_block_the_next_pick()
    {
        var book = new[] { Held("H1"), Held("H2"), Held("H3"), Held("H4") };

        var r = Run([Fc("A", 1), Fc("B", 2)], book: book, k: new FillConstraints(MaxPlanItems: 10, MaxSwing: 9));

        Symbols(r.Primary).ShouldBe(["A"]);
        Reason(r.Reserve.Single(), FillCodes.OpenPositions).Value.ShouldBe(6);
    }

    [Fact]
    public void One_position_per_symbol_blocks_a_second_ticket_on_the_symbol()
    {
        var r = Run([Fc("A", 1, sector: "x"), Fc("A", 2, side: "SELL", sector: "y")]);

        Symbols(r.Primary).ShouldBe(["A"]);
        Reason(r.Reserve.Single(), FillCodes.OnePerSymbol).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void A_symbol_in_the_book_is_rejected_on_its_own()
    {
        var r = Run([Fc("A", 1)], book: [Held("A")]);

        r.Primary.ShouldBeEmpty();
        Reason(r.Rejected.Single(), FillCodes.OnePerSymbol).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void Sell_on_a_long_term_holding_is_rejected_and_buy_is_not()
    {
        var r = Run([Fc("A", 1, side: "SELL", held: true), Fc("B", 2, side: "BUY", held: true)]);

        Symbols(r.Rejected).ShouldBe(["A"]);
        Symbols(r.Primary).ShouldBe(["B"]);
        Reason(r.Rejected[0], FillCodes.HeldLongTerm).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void Sell_with_unknown_holding_status_is_blocked()
    {
        var r = Run([Fc("A", 1, side: "SELL", held: null)]);

        Reason(r.Rejected.Single(), FillCodes.HeldLongTerm).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Per_trade_risk_above_the_limit_is_rejected_at_the_limit_it_passes()
    {
        var r = Run([Fc("A", 1, risk: 1.01), Fc("B", 2, risk: 1.0)]);

        Symbols(r.Rejected).ShouldBe(["A"]);
        Symbols(r.Primary).ShouldBe(["B"]);
        Reason(r.Rejected[0], FillCodes.PerTradeRisk).Limit.ShouldBe(1.0);
    }

    [Fact]
    public void Total_risk_blocks_the_next_pick()
    {
        var r = Run([Fc("A", 1, risk: 0.6), Fc("B", 2, risk: 0.6)], new FillConstraints(MaxPlanItems: 10, MaxTotalOpenRiskPct: 1.0, MaxNetLongRiskPct: 9));

        Symbols(r.Primary).ShouldBe(["A"]);
        Math.Round(Reason(r.Reserve.Single(), FillCodes.TotalRisk).Value!.Value, 6).ShouldBe(1.2);
    }

    [Fact]
    public void Total_risk_counts_the_open_book()
    {
        var r = Run([Fc("A", 1, risk: 0.6)], new FillConstraints(MaxPlanItems: 10, MaxTotalOpenRiskPct: 1.0), [Held("H", risk: 0.5)]);

        Symbols(r.Rejected).ShouldBe(["A"]);
        Reason(r.Rejected[0], FillCodes.TotalRisk).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void Net_long_risk_blocks_a_buy_but_a_sell_still_fits()
    {
        var k = new FillConstraints(MaxPlanItems: 10, MaxNetLongRiskPct: 1.0);

        var r = Run([Fc("A", 1, risk: 0.6), Fc("B", 2, risk: 0.6), Fc("C", 3, side: "SELL", risk: 0.6)], k);

        Symbols(r.Primary).ShouldBe(["A", "C"]);
        Math.Round(Reason(r.Reserve.Single(), FillCodes.NetExposure).Value!.Value, 6).ShouldBe(1.2);
    }

    [Fact]
    public void Net_short_risk_blocks_a_sell()
    {
        var k = new FillConstraints(MaxPlanItems: 10, MaxNetShortRiskPct: 1.0);

        var r = Run([Fc("A", 1, side: "SELL", risk: 0.6), Fc("B", 2, side: "SELL", risk: 0.6)], k);

        Symbols(r.Primary).ShouldBe(["A"]);
        Reason(r.Reserve.Single(), FillCodes.NetExposure).Limit.ShouldBe(1.0);
    }

    [Fact]
    public void Sector_limit_blocks_the_third_pick_in_a_sector()
    {
        var r = Run([Fc("A", 1, sector: "bank"), Fc("B", 2, sector: "bank"), Fc("C", 3, sector: "bank"), Fc("D", 4, sector: "it")]);

        Symbols(r.Primary).ShouldBe(["A", "B", "D"]);
        Reason(r.Reserve.Single(), FillCodes.Sector).Value.ShouldBe(3);
    }

    [Fact]
    public void Intraday_swing_mix_blocks_a_third_of_a_style()
    {
        var r = Run([Fc("A", 1, style: "swing"), Fc("B", 2, style: "swing"), Fc("C", 3, style: "swing"), Fc("D", 4, style: "intraday")], new FillConstraints(MaxPlanItems: 10, MaxOpenPositions: 9));

        Symbols(r.Primary).ShouldBe(["A", "B", "D"]);
        Reason(r.Reserve.Single(), FillCodes.Mix).Value.ShouldBe(3);
    }

    [Fact]
    public void Carried_swing_counts_towards_the_swing_limit()
    {
        var r = Run([Fc("A", 1, style: "swing")], new FillConstraints(MaxPlanItems: 10), [Held("H1"), Held("H2")]);

        Reason(r.Rejected.Single(), FillCodes.Mix).Value.ShouldBe(3);
    }

    [Fact]
    public void Basket_limit_blocks_the_next_pick_from_the_basket()
    {
        var k = new FillConstraints(MaxPlanItems: 10, MaxPerBasket: new Dictionary<string, int> { ["MyChoice"] = 1 });

        var r = Run([Fc("A", 1, baskets: ["MyChoice"]), Fc("B", 2, baskets: ["MyChoice"]), Fc("C", 3, baskets: [])], k);

        Symbols(r.Primary).ShouldBe(["A", "C"]);
        Reason(r.Reserve.Single(), FillCodes.Basket).Value.ShouldBe(2);
    }

    [Fact]
    public void Basket_limit_with_unknown_baskets_blocks()
    {
        var k = new FillConstraints(MaxPlanItems: 10, MaxPerBasket: new Dictionary<string, int> { ["MyChoice"] = 1 });
        var c = Fc("A", 1) with { Baskets = null };

        var r = Run([c], k);

        Reason(r.Rejected.Single(), FillCodes.Basket).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Correlation_above_the_limit_with_a_pick_blocks_the_next_pick()
    {
        var r = Run([Fc("A", 1), Fc("B", 2), Fc("C", 3)], corr: Corr(("A", "B", 0.71), ("A", "C", 0.70), ("B", "C", 0.1)));

        Symbols(r.Primary).ShouldBe(["A", "C"]);
        Reason(r.Reserve.Single(), FillCodes.Correlation).Value.ShouldBe(0.71);
    }

    [Fact]
    public void Correlation_with_an_open_position_blocks_and_is_symmetric()
    {
        var r = Run([Fc("A", 1)], book: [Held("H")], corr: Corr(("H", "A", 0.9)));

        Reason(r.Rejected.Single(), FillCodes.Correlation).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void Unknown_correlation_blocks_the_second_pick_but_not_the_first()
    {
        var r = PortfolioFiller.Fill([Fc("A", 1), Fc("B", 2)], [], null, new FillConstraints());

        Symbols(r.Primary).ShouldBe(["A"]);
        Reason(r.Reserve.Single(), FillCodes.Correlation).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Unknown_candidate_facts_block_and_are_reported_unavailable()
    {
        var r = Run([Fc("A", 1, risk: null), Fc("B", 2) with { Sector = null }, Fc("C", 3, style: "other")]);

        r.Primary.ShouldBeEmpty();
        Reason(r.Rejected[0], FillCodes.PerTradeRisk).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Reason(r.Rejected[0], FillCodes.TotalRisk).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Reason(r.Rejected[0], FillCodes.NetExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Reason(r.Rejected[1], FillCodes.Sector).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Reason(r.Rejected[2], FillCodes.Mix).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Unknown_risk_of_an_open_position_blocks_the_risk_limits()
    {
        var r = Run([Fc("A", 1)], book: [new BookPosition("H", "BUY", "swing", double.NaN, "s", [])]);

        Reason(r.Rejected.Single(), FillCodes.TotalRisk).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Unknown_sector_of_an_open_position_blocks()
    {
        var r = Run([Fc("A", 1)], book: [new BookPosition("H", "BUY", "swing", 0.1, null, [])]);

        Reason(r.Rejected.Single(), FillCodes.Sector).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Unknown_book_takes_nothing()
    {
        var r = PortfolioFiller.Fill([Fc("A", 1), Fc("B", 2)], null, Corr(), new FillConstraints());

        r.Primary.ShouldBeEmpty();
        r.Reserve.ShouldBeEmpty();
        r.Rejected.ShouldAllBe(d => d.Reasons.Single().Code == FillCodes.Book && d.Reasons.Single().Outcome == FilterOutcome.Unavailable);
        r.Rejected.Count.ShouldBe(2);
    }

    [Fact]
    public void A_candidate_without_an_instrument_is_blocked()
    {
        var r = Run([Fc("*", 1)]);

        Reason(r.Rejected.Single(), FillCodes.OnePerSymbol).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Reserve_is_admissible_alone_limited_to_reserve_size_and_the_rest_are_rejected()
    {
        var k = new FillConstraints(MaxPlanItems: 1, ReserveSize: 2);

        var r = Run([Fc("A", 1), Fc("B", 2), Fc("C", 3), Fc("D", 4), Fc("E", 5, risk: 2.0)], k);

        Symbols(r.Primary).ShouldBe(["A"]);
        Symbols(r.Reserve).ShouldBe(["B", "C"]);
        Symbols(r.Rejected).ShouldBe(["D", "E"]);
        Reason(r.Rejected[0], FillCodes.ReserveFull).Limit.ShouldBe(2);
        Reason(r.Rejected[0], FillCodes.PlanItems).Outcome.ShouldBe(FilterOutcome.Fail);
        Reason(r.Rejected[1], FillCodes.PerTradeRisk).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void A_lower_ranked_pick_that_fits_is_taken_after_a_blocked_one()
    {
        var r = Run([Fc("A", 1, sector: "x"), Fc("B", 2, sector: "x"), Fc("C", 3, sector: "x"), Fc("D", 4, sector: "y")]);

        Symbols(r.Primary).ShouldBe(["A", "B", "D"]);
    }

    [Fact]
    public void Output_does_not_depend_on_input_order()
    {
        var cands = new[]
        {
            Fc("A", 1, sector: "x"), Fc("B", 2, sector: "x"), Fc("C", 3, sector: "x"), Fc("D", 4, sector: "y", side: "SELL"),
            Fc("E", 5, style: "swing"), Fc("F", 6, risk: 3.0), Fc("G", 7), Fc("H", 8),
        };
        var corr = Corr(("A", "E", 0.8));
        var k = new FillConstraints(MaxPlanItems: 4, ReserveSize: 2);
        var expected = Run(cands, k, corr: corr);
        var rng = new Random(7);

        for (var i = 0; i < 25; i++)
        {
            var shuffled = cands.OrderBy(_ => rng.Next()).ToArray();

            var r = Run(shuffled, k, corr: corr);

            Symbols(r.Primary).ShouldBe(Symbols(expected.Primary));
            Symbols(r.Reserve).ShouldBe(Symbols(expected.Reserve));
            Symbols(r.Rejected).ShouldBe(Symbols(expected.Rejected));
            r.Reserve.Select(d => d.Reasons.Select(x => x.Code).ToArray()).ShouldBe(expected.Reserve.Select(d => d.Reasons.Select(x => x.Code).ToArray()));
        }
    }

    [Fact]
    public void Inputs_are_not_changed_and_every_candidate_appears_once()
    {
        var cands = new List<FillCandidate> { Fc("B", 2), Fc("A", 1), Fc("C", 3) };
        var book = new List<BookPosition> { Held("H") };
        var copy = cands.ToList();

        var r = PortfolioFiller.Fill(cands, book, Corr(), new FillConstraints(MaxPlanItems: 1));

        cands.ShouldBe(copy);
        book.Count.ShouldBe(1);
        Symbols([.. r.Primary, .. r.Reserve, .. r.Rejected]).Order().ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void Invalid_constraints_are_refused()
    {
        var k = new FillConstraints(MaxPairCorrelation: double.NaN, MaxTotalOpenRiskPct: -1);

        k.Problems().Count.ShouldBe(2);
        Should.Throw<ArgumentException>(() => PortfolioFiller.Fill([], [], null, k));
    }
}
