using XDrishti.Domain.Learning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>BK-08 criteria 3 to 5 (ADR 0260): the cluster cap of the fill. Risks are percent of capital: 1,000,000 of capital with a cap of 1.0 % is 10,000, a book risk of 0.6 is 6,000 and a candidate of 0.5 is 5,000.</summary>
public sealed class PortfolioFillerClusterTests
{
    private static readonly string[] Names = ["A", "B", "C", "D", "E", "F", "G", "H"];

    private static FillCandidate Fc(string symbol, int rank, double? risk = 0.5, string? cluster = null, string? sector = null, string[]? baskets = null)
    {
        var plan = Candidates.Good(symbol) with { Cell = new CellId("S1", "15m", "BUY", "intraday", "fixed") };
        var merged = new MergedCandidate(new Eligibility(plan, EligibilityVerdict.Eligible, [], 0.5), 0, []);
        return new FillCandidate(new RankedCandidate(rank, merged, 1.0 - (rank * 0.01), []), risk, sector ?? "sec-" + symbol, baskets ?? [], false, cluster);
    }

    private static BookPosition Held(string symbol, double risk, string? cluster) => new(symbol, "BUY", "swing", risk, "book-" + symbol, [], cluster);

    private static CorrelationMatrix Corr(params (string, string, double)[] pairs) => new([.. pairs, .. Names.SelectMany(a => Names.Select(b => (a, b, 0.1)))]);

    private static FillConstraints Limits(double? cap, int perSector = 2, IReadOnlyDictionary<string, int>? baskets = null) =>
        new(MaxPlanItems: 10, MaxIntraday: 9, MaxSwing: 9, MaxOpenPositions: 10, MaxTotalOpenRiskPct: 10, MaxNetLongRiskPct: 10, MaxPerSector: perSector, MaxPerBasket: baskets, MaxClusterRiskPct: cap);

    // Everything a result says about the candidates, as text (the lists inside a record compare by reference).
    private static string Shape(FillResult r) => string.Join(";", new[] { r.Primary, r.Reserve, r.Rejected }.Select(g => string.Join("|", g.Select(d => d.Candidate.Symbol + ":" + d.Status + ":" + string.Join(",", d.Checks.Select(c => $"{c.Code}/{c.Outcome}/{c.Value}/{c.Limit}/{c.Detail}"))))));

    private static string[] Symbols(IEnumerable<FillDecision> d) => [.. d.Select(x => x.Candidate.Symbol)];

    [Fact]
    public void A_candidate_that_breaks_the_cap_against_the_open_book_is_not_taken_with_code_and_numbers_and_the_other_cluster_is_taken()
    {
        var book = new[] { Held("B", 0.6, "A") };

        var r = PortfolioFiller.Fill([Fc("A", 1, 0.5, "A"), Fc("C", 2, 0.5, "C")], book, Corr(), Limits(1.0));

        Symbols(r.Primary).ShouldBe(["C"]);
        // Like every other limit of the fill (ADR 0084), a candidate that fails against the open book on its own is rejected, not reserved: a reserve is admissible alone. A cap broken only by this plan's picks is a reserve (next test).
        Symbols(r.Reserve).ShouldBeEmpty();
        var reason = r.Rejected.Single().Reasons.Single();
        reason.Code.ShouldBe(FillCodes.ClusterCap);
        reason.Outcome.ShouldBe(FilterOutcome.Fail);
        reason.Value.ShouldNotBeNull().ShouldBe(1.1, 1e-9);
        reason.Limit.ShouldBe(1.0);
    }

    [Fact]
    public void A_candidate_that_makes_the_cluster_exactly_the_cap_is_taken_because_the_cap_is_not_above()
    {
        var book = new[] { Held("B", 0.6, "A") };

        var r = PortfolioFiller.Fill([Fc("A", 1, 0.4, "A")], book, Corr(), Limits(1.0));

        Symbols(r.Primary).ShouldBe(["A"]);
    }

    [Fact]
    public void Picks_of_the_same_plan_count_toward_the_cluster_and_the_loser_goes_to_reserve_with_the_code()
    {
        var r = PortfolioFiller.Fill([Fc("A", 1, 0.6, "A"), Fc("B", 2, 0.5, "A")], [], Corr(), Limits(1.0));

        Symbols(r.Primary).ShouldBe(["A"]);
        Symbols(r.Reserve).ShouldBe(["B"]);
        r.Reserve[0].Reasons.Single().Code.ShouldBe(FillCodes.ClusterCap);
    }

    [Fact]
    public void With_the_cap_off_the_result_is_identical_whatever_the_cluster_ids()
    {
        var plain = new[] { Fc("A", 1), Fc("B", 2), Fc("C", 3), Fc("D", 4, 0.9) };
        var tagged = plain.Select(c => c with { ClusterId = "X" }).ToArray();
        var book = new[] { Held("E", 0.4, "X") };
        var untaggedBook = new[] { Held("E", 0.4, null) };
        var off = Limits(null, perSector: 1);

        var a = PortfolioFiller.Fill(plain, untaggedBook, Corr(), off);
        var b = PortfolioFiller.Fill(tagged, book, Corr(), off);

        Shape(b).ShouldBe(Shape(a));
        b.Primary.SelectMany(d => d.Checks).ShouldNotContain(c => c.Code == FillCodes.ClusterCap);
    }

    [Fact]
    public void An_unknown_cluster_is_unavailable_never_a_pass_and_blocks()
    {
        var r = PortfolioFiller.Fill([Fc("A", 1, 0.5, null)], [], Corr(), Limits(1.0));

        r.Primary.ShouldBeEmpty();
        var reason = r.Rejected.Single().Reasons.Single();
        reason.Code.ShouldBe(FillCodes.ClusterCap);
        reason.Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void An_open_position_whose_cluster_or_risk_is_unknown_makes_the_check_unavailable_unless_the_known_total_already_fails()
    {
        var unknownCluster = new[] { Held("B", 0.1, null) };
        var unknownRisk = new[] { new BookPosition("B", "BUY", "swing", double.NaN, "book-B", [], "A") };

        var a = PortfolioFiller.Fill([Fc("A", 1, 0.5, "A")], unknownCluster, Corr(), Limits(1.0));
        var b = PortfolioFiller.Fill([Fc("A", 1, 0.5, "A")], unknownRisk, Corr(), Limits(1.0));
        var failing = PortfolioFiller.Fill([Fc("A", 1, 1.2, "A")], unknownCluster, Corr(), Limits(1.0));

        a.Primary.ShouldBeEmpty();
        a.Rejected.Single().Reasons.Select(x => (x.Code, x.Outcome)).ShouldContain((FillCodes.ClusterCap, FilterOutcome.Unavailable));
        b.Primary.ShouldBeEmpty();
        failing.Rejected.Single().Reasons.Single(x => x.Code == FillCodes.ClusterCap).Outcome.ShouldBe(FilterOutcome.Fail);
    }

    [Fact]
    public void The_cluster_cap_is_checked_after_correlation_sector_and_basket_and_the_reasons_keep_that_order()
    {
        var limited = new Dictionary<string, int> { ["bank"] = 0 };
        var book = new[] { new BookPosition("B", "BUY", "swing", 0.6, "banks", [], "A") };
        var candidate = Fc("A", 1, 0.9, "A", sector: "banks", baskets: ["bank"]);

        var r = PortfolioFiller.Fill([candidate], book, Corr(("A", "B", 0.85)), Limits(1.0, perSector: 1, baskets: limited));

        r.Rejected.Single().Reasons.Select(x => x.Code).ShouldBe([FillCodes.Sector, FillCodes.Basket, FillCodes.Correlation, FillCodes.ClusterCap]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(101.0)]
    [InlineData(double.NaN)]
    public void A_cap_that_is_not_above_zero_and_at_most_100_is_a_problem(double cap)
    {
        Limits(cap).Problems().ShouldContain(p => p.Contains("MaxClusterRiskPct", StringComparison.Ordinal));
    }

    [Fact]
    public void Property_the_fill_never_takes_a_cluster_above_the_cap_when_it_is_on()
    {
        var random = new Random(5);
        for (var round = 0; round < 40; round++)
        {
            var cap = 0.5 + (random.NextDouble() * 1.5);
            var clusters = new[] { "A", "B", "C" };
            var cands = Names.Select((n, i) => Fc(n, i + 1, Math.Round(0.1 + (random.NextDouble() * 0.6), 2), clusters[random.Next(3)])).ToArray();
            var book = new[] { Held("H", Math.Round(random.NextDouble() * 0.5, 2), clusters[random.Next(3)]) };

            var r = PortfolioFiller.Fill(cands, book, Corr(), Limits(cap, perSector: 5));

            foreach (var cluster in clusters)
            {
                var total = book.Where(b => b.ClusterId == cluster).Sum(b => b.RiskPct) + r.Primary.Where(d => d.Candidate.ClusterId == cluster).Sum(d => d.Candidate.RiskPct!.Value);
                total.ShouldBeLessThanOrEqualTo(cap + 1e-9);
            }
        }
    }
}
