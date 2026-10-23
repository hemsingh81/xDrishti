using XDrishti.Domain.Learning;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>Hand table per filter (spec P7-01): at the limit, just below, and missing.</summary>
public sealed class EligibilityFiltersTests
{
    private static readonly SelectionRules Rules = new();

    private static FilterResult Run(PlanCandidate c, string code) =>
        EligibilityFilters.Evaluate(c, Rules).Results.Single(r => r.Code == code);

    [Fact]
    public void A_candidate_that_meets_every_limit_is_eligible_and_provisional_below_active()
    {
        var e = EligibilityFilters.Evaluate(Candidates.Good(), Rules);

        e.Verdict.ShouldBe(EligibilityVerdict.Eligible);
        e.Provisional.ShouldBeTrue();
        e.ConservativeExpectancyR!.Value.ShouldBe(0.5, 1e-12);
        e.Caveats.ShouldBeEmpty();
        e.Results.Select(r => r.Code).ShouldBe(FilterCodes.All);
    }

    [Fact]
    public void An_active_cell_is_not_provisional()
    {
        EligibilityFilters.Evaluate(Candidates.Good(state: CellState.Active), Rules).Provisional.ShouldBeFalse();
    }

    [Theory]
    [InlineData(CellState.Research, FilterOutcome.Fail)]
    [InlineData(CellState.Candidate, FilterOutcome.Pass)]
    [InlineData(CellState.Paper, FilterOutcome.Pass)]
    [InlineData(CellState.Active, FilterOutcome.Pass)]
    [InlineData(CellState.Paused, FilterOutcome.Fail)]
    [InlineData(CellState.Retired, FilterOutcome.Fail)]
    public void Cell_state_table_with_minimum_candidate(CellState state, FilterOutcome expected)
    {
        Run(Candidates.Good(state: state), FilterCodes.CellState).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void A_higher_minimum_state_rejects_a_candidate_cell()
    {
        var e = EligibilityFilters.Evaluate(Candidates.Good(state: CellState.Candidate), new SelectionRules(MinState: CellState.Active));

        e.Results.Single(r => r.Code == FilterCodes.CellState).Outcome.ShouldBe(FilterOutcome.Fail);
        e.Verdict.ShouldBe(EligibilityVerdict.Rejected);
    }

    [Fact]
    public void An_unknown_state_is_unavailable_and_holds_the_candidate()
    {
        var e = EligibilityFilters.Evaluate(Candidates.Good(state: null), Rules);

        e.Results.Single(r => r.Code == FilterCodes.CellState).Outcome.ShouldBe(FilterOutcome.Unavailable);
        e.Verdict.ShouldBe(EligibilityVerdict.Held);
    }

    [Theory]
    [InlineData(30, FilterOutcome.Pass)]
    [InlineData(29, FilterOutcome.Fail)]
    public void Sample_table(int n, FilterOutcome expected)
    {
        Run(Candidates.Good(stats: Candidates.Stats(n: n)), FilterCodes.Sample).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.45, FilterOutcome.Pass)]
    [InlineData(0.4499, FilterOutcome.Fail)]
    public void Win_rate_uses_the_shrunk_posterior(double posterior, FilterOutcome expected)
    {
        Run(Candidates.Good(stats: Candidates.Stats(posterior: posterior)), FilterCodes.WinRate).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.5, 1.2, -1.0, 0.1, FilterOutcome.Pass)]
    [InlineData(0.5, 1.1, -1.0, 0.05, FilterOutcome.Fail)]
    [InlineData(0.5, 2.0, -1.0, 0.5, FilterOutcome.Pass)]
    public void Expectancy_is_the_lower_win_rate_times_the_average_win_plus_the_rest_times_the_average_loss(double lower, double win, double loss, double expected, FilterOutcome outcome)
    {
        var r = Run(Candidates.Good(stats: Candidates.Stats(lower: lower, avgWin: win, avgLoss: loss)), FilterCodes.Expectancy);

        r.Value!.Value.ShouldBe(expected, 1e-9);
        r.Outcome.ShouldBe(outcome);
    }

    [Fact]
    public void Expectancy_at_exactly_the_limit_passes()
    {
        // 0.4 * 1.5 + 0.6 * -0.8333.. is not exact; use 0.5 * 1.2 + 0.5 * -1.0 = 0.1
        Run(Candidates.Good(stats: Candidates.Stats(lower: 0.5, avgWin: 1.2, avgLoss: -1.0)), FilterCodes.Expectancy).Outcome.ShouldBe(FilterOutcome.Pass);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_missing_average_makes_expectancy_unavailable(bool noWin, bool noLoss)
    {
        var stats = Candidates.Stats(avgWin: noWin ? null : 2.0, avgLoss: noLoss ? null : -1.0);

        Run(Candidates.Good(stats: stats), FilterCodes.Expectancy).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Gross_statistics_are_reduced_by_the_cost_and_unavailable_without_it()
    {
        var gross = Candidates.Stats(costed: false);

        Run(Candidates.Good(stats: gross, cost: 0.05), FilterCodes.Expectancy).Value!.Value.ShouldBe(0.45, 1e-9);
        Run(Candidates.Good(stats: gross, cost: null), FilterCodes.Expectancy).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(1.5, FilterOutcome.Pass)]
    [InlineData(1.49, FilterOutcome.Fail)]
    public void Reward_risk_table(double rr, FilterOutcome expected)
    {
        Run(Candidates.Good(rr: rr), FilterCodes.RewardRisk).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.10, FilterOutcome.Pass)]
    [InlineData(0.11, FilterOutcome.Fail)]
    public void Cost_table(double cost, FilterOutcome expected)
    {
        Run(Candidates.Good(cost: cost), FilterCodes.Cost).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(10_000_000, FilterOutcome.Pass)]
    [InlineData(9_999_999, FilterOutcome.Fail)]
    public void Liquidity_table(double turnover, FilterOutcome expected)
    {
        Run(Candidates.Good(turnover: turnover), FilterCodes.Liquidity).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Missing_required_data_is_unavailable_never_a_pass_and_holds_the_candidate()
    {
        var e = EligibilityFilters.Evaluate(Candidates.Good(rr: null, cost: null, turnover: null), Rules);

        e.Results.Where(r => r.Code is FilterCodes.RewardRisk or FilterCodes.Cost or FilterCodes.Liquidity).ShouldAllBe(r => r.Outcome == FilterOutcome.Unavailable);
        e.Verdict.ShouldBe(EligibilityVerdict.Held);
        e.Reasons.Count.ShouldBe(3);
    }

    [Fact]
    public void Not_a_number_is_unavailable()
    {
        Run(Candidates.Good(rr: double.NaN), FilterCodes.RewardRisk).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Run(Candidates.Good(stats: Candidates.Stats(posterior: double.NaN)), FilterCodes.WinRate).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(FilterCodes.Event)]
    [InlineData(FilterCodes.FnoBan)]
    [InlineData(FilterCodes.Surveillance)]
    public void Unknown_event_ban_and_surveillance_hold_the_candidate_by_default(string code)
    {
        var c = Candidates.Good(eventBlock: null, ban: null, surveillance: null);

        var e = EligibilityFilters.Evaluate(c, Rules);

        e.Results.Single(r => r.Code == code).Outcome.ShouldBe(FilterOutcome.Unavailable);
        e.Verdict.ShouldBe(EligibilityVerdict.Held);
        e.Caveats.ShouldBeEmpty();
    }

    [Fact]
    public void An_owner_can_loosen_the_filters_and_the_unavailable_ones_stay_as_caveats()
    {
        var rules = new SelectionRules(OptionalFilters: [FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);

        var e = EligibilityFilters.Evaluate(Candidates.Good(eventBlock: null, ban: null, surveillance: null), rules);

        e.Verdict.ShouldBe(EligibilityVerdict.Eligible);
        e.Caveats.ShouldBe([FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);
        EligibilityFilters.Evaluate(Candidates.Good(ban: null), new SelectionRules(OptionalFilters: [FilterCodes.Event])).Verdict.ShouldBe(EligibilityVerdict.Held);
    }

    [Theory]
    [InlineData(true, null, null)]
    [InlineData(null, true, null)]
    [InlineData(null, null, true)]
    public void A_known_exclusion_always_rejects(bool? ev, bool? ban, bool? surveillance)
    {
        var e = EligibilityFilters.Evaluate(Candidates.Good(eventBlock: ev ?? false, ban: ban ?? false, surveillance: surveillance ?? false), Rules);

        e.Verdict.ShouldBe(EligibilityVerdict.Rejected);
    }

    [Fact]
    public void No_look_ahead_statistics_as_of_the_decision_date_or_later_are_unavailable()
    {
        var same = Candidates.Good(stats: Candidates.Stats(asOf: Candidates.Decision));
        var later = Candidates.Good(stats: Candidates.Stats(asOf: Candidates.Decision.AddDays(3)));
        var before = Candidates.Good(stats: Candidates.Stats(asOf: Candidates.Decision.AddDays(-1)));

        foreach (var c in new[] { same, later })
        {
            var e = EligibilityFilters.Evaluate(c, Rules);
            e.Verdict.ShouldBe(EligibilityVerdict.Held);
            e.Results.Where(r => r.Code is FilterCodes.StatsAsOf or FilterCodes.Sample or FilterCodes.WinRate or FilterCodes.Expectancy)
                .ShouldAllBe(r => r.Outcome == FilterOutcome.Unavailable);
        }

        EligibilityFilters.Evaluate(before, Rules).Verdict.ShouldBe(EligibilityVerdict.Eligible);
    }

    [Fact]
    public void No_statistics_is_unavailable()
    {
        var c = Candidates.Good() with { Stats = null };

        EligibilityFilters.Evaluate(c, Rules).Verdict.ShouldBe(EligibilityVerdict.Held);
    }

    [Fact]
    public void A_fail_wins_over_an_unavailable()
    {
        var e = EligibilityFilters.Evaluate(Candidates.Good(rr: null, cost: 0.5), Rules);

        e.Verdict.ShouldBe(EligibilityVerdict.Rejected);
    }

    [Fact]
    public void The_advisory_model_value_changes_nothing()
    {
        var plain = EligibilityFilters.Evaluate(Candidates.Good(), Rules);
        var withModel = EligibilityFilters.Evaluate(Candidates.Good(advisory: 0.01), Rules);

        withModel.Verdict.ShouldBe(plain.Verdict);
        withModel.Results.ShouldBe(plain.Results);
    }

    [Fact]
    public void Rules_are_validated()
    {
        new SelectionRules().Problems().ShouldBeEmpty();
        new SelectionRules(MinWinRate: 2, MinSample: 0, MinState: CellState.Retired, OptionalFilters: ["nope"]).Problems().Count.ShouldBe(4);
        new SelectionRules(MaxCostR: double.NaN).Problems().Count.ShouldBe(1);
    }
}
