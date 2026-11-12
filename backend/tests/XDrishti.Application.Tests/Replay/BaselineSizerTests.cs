using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Replay;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-02, AC 1 and 2: the baseline takes every candidate at r = min(risk per trade, headroom / n), under the live allocator's limits, and blocks nothing by selection.</summary>
public sealed class BaselineSizerTests
{
    private static readonly CellId Cell = new("S1", "1D", "BUY", "swing", "FIXED_RR{rr=2}", Expression: Expression.Cash);

    private static TicketCandidate Candidate(string symbol, double entry = 500, double stop = 490, string side = "BUY")
    {
        var draft = new TicketDraft(TicketVerdict.Conditional, symbol, side, "S1", "h", new DateOnly(2026, 10, 1), SetupTruth.True, SetupTruth.True, SetupTruth.False, SetupTruth.True, [], "r", "t", "s", "tg", "v", stop, entry, 2.0, null, []);
        return new TicketCandidate(draft, Cell, null, CellState.Active);
    }

    private static LedgerFigures Figures(decimal equity = 1_000_000m, decimal openRiskPct = 1.0m, decimal? funds = null, decimal? lastSessionPct = null) => new(0, null, 0, "gross", 0, 0, equity, 0, funds ?? equity, 0, openRiskPct, 0, lastSessionPct, 0, null);

    private static IReadOnlyList<TicketCandidate> Eight() => [.. Enumerable.Range(1, 8).Select(i => Candidate("SYM" + i))];

    private static HashSet<string> None() => new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Eight_candidates_with_one_percent_open_risk_risk_a_quarter_percent_each_and_take_250_shares()
    {
        var outcome = BaselineSizer.Size(Eight(), Figures(), ReplayRiskProfile.Standard, None());

        outcome.RiskPct.ShouldBe(0.25, 1e-9);
        outcome.Taken.Count.ShouldBe(8);
        outcome.Taken.ShouldAllBe(t => t.Item.Allocations[0].Quantity == 250); // Rs 2,500 / Rs 10 = 250 shares, Rs 1,25,000 = 12.5 % of equity
        outcome.Blocked.ShouldBeEmpty();
    }

    [Fact]
    public void Three_candidates_risk_the_half_percent_of_the_profile_and_take_500_shares()
    {
        var outcome = BaselineSizer.Size([.. Eight().Take(3)], Figures(), ReplayRiskProfile.Standard, None());

        outcome.RiskPct.ShouldBe(0.5, 1e-9);
        outcome.Taken.Count.ShouldBe(3);
        outcome.Taken.ShouldAllBe(t => t.Item.Allocations[0].Quantity == 500); // Rs 2,50,000 = 25 % of equity, the cap per trade
    }

    [Fact]
    public void With_no_headroom_nothing_is_taken_and_every_candidate_is_blocked_as_total_risk()
    {
        var outcome = BaselineSizer.Size(Eight(), Figures(openRiskPct: 3.0m), ReplayRiskProfile.Standard, None());

        outcome.Taken.ShouldBeEmpty();
        outcome.Blocked.Count.ShouldBe(8);
        outcome.Blocked.ShouldAllBe(b => b.Reason == BlockReason.TotalRisk);
    }

    [Theory]
    [InlineData(-1.5, true)]
    [InlineData(-2.0, true)]
    [InlineData(-1.49, false)]
    [InlineData(0.8, false)]
    public void A_daily_loss_at_or_past_the_profile_limit_stops_every_new_candidate(double lastSessionPct, bool stopped)
    {
        var outcome = BaselineSizer.Size(Eight(), Figures(lastSessionPct: (decimal)lastSessionPct), ReplayRiskProfile.Standard, None());

        (outcome.Taken.Count == 0).ShouldBe(stopped);
        if (stopped)
        {
            outcome.Blocked.ShouldAllBe(b => b.Detail == "daily_loss");
        }
    }

    [Fact]
    public void Every_candidate_is_taken_and_no_selection_reason_ever_appears()
    {
        var found = Eight();
        var outcome = BaselineSizer.Size([.. found.Take(6)], Figures(), ReplayRiskProfile.Standard, None());

        outcome.Taken.Count.ShouldBe(6);
        outcome.Blocked.Select(b => b.Reason).ShouldNotContain(BlockReason.SmartRule);
        outcome.Blocked.Select(b => b.Reason).ShouldNotContain(BlockReason.MaxPositions);
        outcome.Blocked.Select(b => b.Reason).ShouldNotContain(BlockReason.ClusterCap);
    }

    [Fact]
    public void A_symbol_already_open_or_met_twice_is_not_doubled_and_a_candidate_without_levels_has_no_quantity()
    {
        var found = new List<TicketCandidate> { Candidate("AAA"), Candidate("AAA"), Candidate("HELD"), Candidate("NOLEVEL", entry: 500, stop: 500) };

        var outcome = BaselineSizer.Size(found, Figures(), ReplayRiskProfile.Standard, new HashSet<string>(["held"], StringComparer.OrdinalIgnoreCase));

        outcome.Taken.Select(t => t.Item.Symbol).ShouldBe(["AAA"]);
        outcome.Blocked.Select(b => (b.Symbol, b.Reason, b.Detail)).Order().ShouldBe([("AAA", BlockReason.Capital, "already_held"), ("HELD", BlockReason.Capital, "already_held"), ("NOLEVEL", BlockReason.Capital, "no_quantity")]);
    }

    [Fact]
    public void Free_cash_limits_the_later_candidates()
    {
        var outcome = BaselineSizer.Size([.. Eight().Take(3)], Figures(funds: 300_000m), ReplayRiskProfile.Standard, None());

        outcome.Taken.ShouldNotBeEmpty();
        outcome.Taken.Sum(t =>t.Item.Allocations[0].Quantity * 500m).ShouldBeLessThanOrEqualTo(300_000m);
    }

    [Fact]
    public void The_order_is_by_symbol_so_the_same_inputs_give_the_same_items()
    {
        var a = BaselineSizer.Size([.. Eight().Reverse()], Figures(), ReplayRiskProfile.Standard, None());
        var b = BaselineSizer.Size(Eight(), Figures(), ReplayRiskProfile.Standard, None());

        a.Taken.Select(t => (t.Item.Rank, t.Item.Symbol)).ShouldBe([.. b.Taken.Select(t => (t.Item.Rank, t.Item.Symbol))]);
    }
}
