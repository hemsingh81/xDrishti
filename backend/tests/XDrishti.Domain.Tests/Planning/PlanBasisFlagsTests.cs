using System.Globalization;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;
using XDrishti.Domain.Tests.Selection;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>The stored flags of a plan by hand (spec P8-14, ADR 0113): values from the smart plan, banner text byte for byte, a pure function of the inputs.</summary>
public sealed class PlanBasisFlagsTests
{
    private static readonly DateOnly Day = Candidates.Decision;
    private static readonly DateOnly Before = Day.AddDays(-1);
    private static readonly SmartRulesOptions Opt = new();

    private static IReadOnlyList<RankedCandidate> Four(bool costed = true) =>
        CandidateSelector.Select(
            new[] { ("AAA", 0.50), ("BBB", 0.48), ("CCC", 0.46), ("DDD", 0.44) }
                .Select((x, i) => Candidates.Good(x.Item1, "S" + (i + 1), "BUY", stats: Candidates.Stats("S" + (i + 1), lower: x.Item2, costed: costed))),
            new SelectionRules()).Ranked;

    private static List<TakenTrade> Trades(int n, double r) => [.. Enumerable.Range(0, n).Select(i => new TakenTrade("T" + i.ToString("000", CultureInfo.InvariantCulture), Before.AddDays(-n + i), r))];

    private static SmartInputs Inputs(MarketGateInput? gate, IReadOnlyList<TakenTrade>? trades, CorrelationTable? table = null) =>
        new(gate, trades, table, null);

    private static PlanBasisFlags Flags(SmartPlan plan, string book = PlanBasisFlags.OpenBookKnown) => PlanBasisFlags.From(plan, book, plan.Primary);

    [Fact]
    public void Gate_and_throttle_unavailable_with_the_missing_inputs_and_the_banner_text_unchanged()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Inputs(null, null, new CorrelationTable([])), Opt);
        var f = Flags(plan, PlanBasisFlags.OpenBookUnavailable);

        f.SchemaVersion.ShouldBe(1);
        f.Gate.ShouldBe("unavailable");
        f.GateMissing.ShouldNotBeEmpty();
        f.Throttle.ShouldBe("unavailable");
        f.OpenBook.ShouldBe("unavailable");
        f.Banner.ShouldBe(plan.Flags.Select(x => x.Text));
        f.Banner.ShouldContain(t => t.Contains("market gate unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Reduced_gate_keeps_the_risk_multiplier_and_a_cut_throttle_is_cut()
    {
        var reduced = SmartRulesEngine.Apply(Four(), Day, Inputs(new MarketGateInput(Before, 19, 0.5, false), Trades(20, 1), new CorrelationTable([])), Opt);
        var f = Flags(reduced);
        f.Gate.ShouldBe("reduced");
        f.Throttle.ShouldBe("clear");
        f.RiskMultiplier.ShouldBe(0.75, 1e-9);
        f.Correlation.ShouldBe("applied");
        f.CorrelationUnavailablePairs.ShouldBe(reduced.CorrelationUnavailablePairs); // pairs with no figure in the empty table

        var cut = SmartRulesEngine.Apply(Four(), Day, Inputs(new MarketGateInput(Before, 15, 0.5, false), Trades(20, -1), new CorrelationTable([])), Opt);
        Flags(cut).Throttle.ShouldBe("cut");
        Flags(cut).Gate.ShouldBe("normal");
    }

    [Fact]
    public void No_trade_gate_and_a_rule_that_removed_half_are_recorded()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Inputs(new MarketGateInput(Before, 25, 0.5, false), Trades(20, 1), new CorrelationTable([])), Opt);
        var f = Flags(plan);
        f.Gate.ShouldBe("no_trade");
        f.RuleRemovedHalf.ShouldBeTrue();
        f.Banner.ShouldContain(t => t.Contains("half or more", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_correlation_table_is_unavailable_and_open_book_states_pass_through()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Inputs(new MarketGateInput(Before, 15, 0.5, false), Trades(20, 1), null), Opt);
        Flags(plan).Correlation.ShouldBe("unavailable");
        Flags(plan, PlanBasisFlags.OpenBookAssumedEmpty).OpenBook.ShouldBe("assumed_empty");
    }

    [Fact]
    public void Cost_basis_follows_the_chosen_candidates()
    {
        var plan = SmartRulesEngine.Apply(Four(), Day, Inputs(new MarketGateInput(Before, 15, 0.5, false), Trades(20, 1), new CorrelationTable([])), Opt);
        Flags(plan).CostBasis.ShouldBe("costed_unconfirmed");
        PlanBasisFlags.From(plan, "known", Four(costed: false)).CostBasis.ShouldBe("gross");
        PlanBasisFlags.From(plan, "known", [.. Four(true).Take(2), .. Four(false).Skip(2)]).CostBasis.ShouldBe("mixed");
        PlanBasisFlags.From(plan, "known", []).CostBasis.ShouldBe("unknown");
    }

    [Fact]
    public void Flags_are_a_pure_function_of_the_inputs()
    {
        var inputs = Inputs(new MarketGateInput(Before, 19, 0.5, false), Trades(20, -1), new CorrelationTable([]));
        var a = Flags(SmartRulesEngine.Apply(Four(), Day, inputs, Opt));
        var b = Flags(SmartRulesEngine.Apply(Four(), Day, inputs, Opt));
        b.Banner.ShouldBe(a.Banner);
        (b with { Banner = a.Banner, GateMissing = a.GateMissing }).ShouldBe(a);
    }
}
