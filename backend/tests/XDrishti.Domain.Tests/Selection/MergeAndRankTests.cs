using XDrishti.Domain.Learning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>Merge and rank by hand (spec P7-01). Expectancy of a candidate with lower bound p is 3p - 1 (average win 2, loss -1): 0.5 gives 0.5, 0.4 gives 0.2, 0.45 gives 0.35.</summary>
public sealed class MergeAndRankTests
{
    private static readonly SelectionRules Rules = new();

    private static PlanCandidate C(string symbol, string strategy, double lower, string side = "BUY", string tf = "15m", int n = 40, double? regime = null, double? corr = null) =>
        Candidates.Good(symbol, strategy, side, tf, stats: Candidates.Stats(strategy, n: n, lower: lower), regime: regime, corr: corr);

    private static SelectionResult Select(params PlanCandidate[] c) => CandidateSelector.Select(c, Rules);

    [Fact]
    public void Same_symbol_and_side_keeps_the_highest_expectancy_and_drops_the_rest_with_a_code()
    {
        var r = Select(C("AAA", "S1", 0.40), C("AAA", "S2", 0.50), C("AAA", "S3", 0.45));

        r.Ranked.Count.ShouldBe(1);
        r.Ranked[0].Merged.Winner.Candidate.Cell.Strategy.ShouldBe("S2");
        r.Ranked[0].Merged.Confluence.ShouldBe(2);
        r.Dropped.Select(d => d.Code).ShouldAllBe(c => c == "merged_duplicate");
        r.Dropped.Count.ShouldBe(2);
    }

    [Fact]
    public void Confluence_counts_other_strategies_not_other_timeframes_and_is_capped()
    {
        var one = Select(C("AAA", "S1", 0.50), C("AAA", "S1", 0.45, tf: "5m"));
        var many = Select(C("AAA", "S1", 0.50), C("AAA", "S2", 0.45), C("AAA", "S3", 0.44), C("AAA", "S4", 0.43));

        one.Ranked[0].Merged.Confluence.ShouldBe(0);
        many.Ranked[0].Merged.Confluence.ShouldBe(2);
    }

    [Fact]
    public void Equal_expectancy_ties_break_on_win_rate_then_trades_then_cell_key()
    {
        var a = C("AAA", "SB", 0.50, n: 40);
        var b = C("AAA", "SA", 0.50, n: 40);
        var more = C("AAA", "SC", 0.50, n: 50);

        Select(a, b).Ranked[0].Merged.Winner.Candidate.Cell.Strategy.ShouldBe("SA");
        Select(a, b, more).Ranked[0].Merged.Winner.Candidate.Cell.Strategy.ShouldBe("SC");
    }

    [Fact]
    public void Opposite_sides_keep_the_stronger_when_it_leads_by_more_than_the_margin()
    {
        var r = Select(C("AAA", "S1", 0.50), C("AAA", "S2", 0.40, side: "SELL"));

        r.Ranked.Single().Merged.Winner.Candidate.Side.ShouldBe("BUY");
        r.Dropped.Single().Code.ShouldBe("conflict_lost");
    }

    [Fact]
    public void Opposite_sides_that_are_close_are_both_dropped()
    {
        // 0.50 -> 0.5 R, 0.49 -> 0.47 R: lead 0.03 is not above 0.05
        var r = Select(C("AAA", "S1", 0.50), C("AAA", "S2", 0.49, side: "SELL"));

        r.Ranked.ShouldBeEmpty();
        r.Dropped.Select(d => d.Code).ShouldBe(["conflict_close", "conflict_close"]);
    }

    [Fact]
    public void A_lead_exactly_at_the_margin_is_still_close()
    {
        var rules = new SelectionRules(ConflictMargin: 0.15);
        var c = new[] { C("AAA", "S1", 0.50), C("AAA", "S2", 0.45, side: "SELL") };

        CandidateSelector.Select(c, rules).Ranked.ShouldBeEmpty();
    }

    [Fact]
    public void The_drop_both_policy_drops_even_a_clear_leader()
    {
        var rules = new SelectionRules(ConflictPolicy: ConflictPolicy.DropBoth);

        var r = CandidateSelector.Select([C("AAA", "S1", 0.50), C("AAA", "S2", 0.42, side: "SELL")], rules);

        r.Ranked.ShouldBeEmpty();
        r.Dropped.ShouldAllBe(d => d.Code == "conflict_dropped");
    }

    [Fact]
    public void Different_symbols_do_not_conflict()
    {
        Select(C("AAA", "S1", 0.50), C("BBB", "S1", 0.50, side: "SELL")).Ranked.Count.ShouldBe(2);
    }

    [Fact]
    public void Rejected_and_held_candidates_never_reach_the_merge_and_keep_their_reasons()
    {
        var weak = C("AAA", "S1", 0.30);
        var held = Candidates.Good("BBB", rr: null);

        var r = Select(weak, held, C("CCC", "S1", 0.50));

        r.Ranked.Single().Merged.Winner.Candidate.Symbol.ShouldBe("CCC");
        r.NotEligible.Select(e => e.Verdict).ShouldBe([EligibilityVerdict.Rejected, EligibilityVerdict.Held]);
        r.NotEligible.ShouldAllBe(e => e.Reasons.Count > 0);
    }

    [Fact]
    public void Rank_orders_by_score_with_confluence_regime_correlation_and_event_terms()
    {
        // AAA 0.5 + 0.05*1 = 0.55 ; BBB 0.5 + 0.05*0.8 (regime) = 0.54 ; CCC 0.5 - 0.1*0.3 = 0.47
        var r = Select(
            C("CCC", "S1", 0.50, corr: 0.3),
            C("BBB", "S1", 0.50, regime: 0.8),
            C("AAA", "S1", 0.50),
            C("AAA", "S2", 0.45));

        r.Ranked.Select(x => x.Merged.Winner.Candidate.Symbol).ShouldBe(["AAA", "BBB", "CCC"]);
        r.Ranked[0].Score.ShouldBe(0.55, 1e-9);
        r.Ranked[1].Score.ShouldBe(0.54, 1e-9);
        r.Ranked[2].Score.ShouldBe(0.47, 1e-9);
        r.Ranked.Select(x => x.Rank).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Missing_score_inputs_count_as_zero_and_are_listed()
    {
        var r = Select(C("AAA", "S1", 0.50));

        r.Ranked[0].MissingInputs.ShouldBe(["regime_fit", "correlation_with_book", "event_risk"]);
        r.Ranked[0].Score.ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void Equal_scores_fall_to_symbol_order()
    {
        var r = Select(C("ZZZ", "S1", 0.50), C("AAA", "S1", 0.50), C("MMM", "S1", 0.50));

        r.Ranked.Select(x => x.Merged.Winner.Candidate.Symbol).ShouldBe(["AAA", "MMM", "ZZZ"]);
    }

    [Fact]
    public void The_advisory_model_value_does_not_change_the_rank_but_is_carried()
    {
        var plain = Select(C("AAA", "S1", 0.50), C("BBB", "S1", 0.45));
        var withModel = Select(Candidates.Good("AAA", advisory: 0.01), C("BBB", "S1", 0.45) with { AdvisoryPWin = 0.99 });

        withModel.Ranked.Select(x => x.Merged.Winner.Candidate.Symbol).ShouldBe(plain.Ranked.Select(x => x.Merged.Winner.Candidate.Symbol));
        withModel.Ranked[1].AdvisoryPWin.ShouldBe(0.99);
        withModel.Ranked.Select(x => x.Merged.Winner.Candidate.Cell.Key).ShouldBe(plain.Ranked.Select(x => x.Merged.Winner.Candidate.Cell.Key));
        withModel.NotEligible.Count.ShouldBe(plain.NotEligible.Count);
    }

    [Fact]
    public void A_missing_event_filter_lists_event_risk_as_missing_and_counts_it_as_zero()
    {
        var loose = new SelectionRules(OptionalFilters: [FilterCodes.Event]);
        var r = CandidateSelector.Select([Candidates.Good("AAA", eventBlock: null, eventRisk: 0.9, regime: 0.5, corr: 0.1)], loose).Ranked[0];

        r.MissingInputs.ShouldBe(["event_risk"]);
        r.Caveats.ShouldBe([FilterCodes.Event]);
        r.Explanation.ShouldContain("event_risk");
        r.Score.ShouldBe(0.5 + (0.05 * 0.5) - (0.10 * 0.1), 1e-9);
    }

    [Fact]
    public void The_placeholder_symbol_cannot_be_merged()
    {
        var cell = EligibilityFilters.Evaluate(Candidates.Good("*"), Rules);

        Should.Throw<ArgumentException>(() => CandidateMerger.Merge([cell], Rules));
    }

    [Fact]
    public void Provisional_is_set_below_active_only()
    {
        var r = Select(Candidates.Good("AAA", state: CellState.Active), Candidates.Good("BBB", state: CellState.Candidate));

        r.Ranked.Single(x => x.Merged.Winner.Candidate.Symbol == "AAA").Provisional.ShouldBeFalse();
        r.Ranked.Single(x => x.Merged.Winner.Candidate.Symbol == "BBB").Provisional.ShouldBeTrue();
    }

    [Fact]
    public void The_result_is_the_same_for_every_input_order()
    {
        PlanCandidate[] input =
        [
            C("AAA", "S1", 0.50), C("AAA", "S2", 0.45), C("BBB", "S1", 0.45, side: "SELL"), C("BBB", "S3", 0.30), C("CCC", "S1", 0.50),
            C("CCC", "S1", 0.50, tf: "5m"), Candidates.Good("DDD", rr: null), C("EEE", "S2", 0.50, side: "SELL"), C("EEE", "S1", 0.49),
        ];
        string Key(SelectionResult r) => string.Join(';', r.Ranked.Select(x => $"{x.Rank}:{x.Merged.Winner.Candidate.Symbol}:{x.Merged.Winner.Candidate.Cell.Key}:{x.Score:F9}"))
            + "|" + string.Join(';', r.Dropped.Select(d => d.Candidate.Cell.Key + d.Code))
            + "|" + string.Join(';', r.NotEligible.Select(e => e.Candidate.Cell.Key + e.Verdict));
        var expected = Key(CandidateSelector.Select(input, Rules));
        var random = new Random(7);

        for (var i = 0; i < 25; i++)
        {
            var shuffled = input.OrderBy(_ => random.Next()).ToArray();
            Key(CandidateSelector.Select(shuffled, Rules)).ShouldBe(expected);
        }
    }

    [Fact]
    public void Statistics_from_after_the_decision_date_never_produce_a_ranked_candidate()
    {
        var late = Candidates.Good("AAA", stats: Candidates.Stats(asOf: Candidates.Decision.AddDays(1)));

        var r = Select(late);

        r.Ranked.ShouldBeEmpty();
        r.NotEligible.Single().Verdict.ShouldBe(EligibilityVerdict.Held);
    }
}
