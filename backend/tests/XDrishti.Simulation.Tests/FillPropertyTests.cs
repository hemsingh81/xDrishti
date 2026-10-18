using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 19 of P3-03b over the 500 seeded days: fill timing and range. Every fill minute and exit minute holds a trade; the fill minute starts at or after the decision instant, at most 5 minutes after it and before
/// the square-off; the exit minute is on the entry date and not before the fill minute; every square-off is at the first minute that holds a trade at or after <c>Q</c> (the test's own scan); every exact fill price lies in
/// its minute or is its order's own level; in modelled mode every filled price is on the tick grid and within <c>a + tick</c> of the exact one; <c>−MAE ≤ RExact ≤ MFE</c>; and (AC5) <c>risk &gt; 0</c>. The check is
/// <see cref="FillTimingCheck"/>, which the signal-minute control of criterion 20(c)(ii) reuses.
/// </summary>
public sealed class FillPropertyTests
{
    private static readonly FillRules None = FillRules.Create(0.1, SlippageConfig.None).Value;

    [Fact]
    public void Every_fill_and_exit_of_the_500_days_obeys_the_timing_range_grid_and_excursion_rules_with_the_doc_16_slippage()
    {
        var checkedTrades = 0;
        var violations = new List<string>();
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, FillRules.Default))
        {
            violations.AddRange(FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate, walk.Outcome, FillRules.Default));
            checkedTrades += walk.Trade is null ? 0 : 1;
        }

        violations.ShouldBeEmpty();
        checkedTrades.ShouldBeGreaterThanOrEqualTo(1_500, "the property must check trades, not skip them all");
    }

    [Fact]
    public void Every_fill_and_exit_of_the_500_days_obeys_the_same_rules_with_no_slippage_and_the_filled_prices_are_the_exact_ones()
    {
        var checkedTrades = 0;
        var violations = new List<string>();
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, None))
        {
            violations.AddRange(FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate, walk.Outcome, None));
            checkedTrades += walk.Trade is null ? 0 : 1;
        }

        violations.ShouldBeEmpty();
        checkedTrades.ShouldBeGreaterThanOrEqualTo(1_500);
    }

    [Fact]
    public void Every_seeded_trade_has_a_risk_above_zero_and_the_entries_through_the_stop_were_refused_not_traded()
    {
        var walks = WalkProperties.WalkAll(SeededDays.All, FillRules.Default);

        foreach (var walk in walks.Where(w => w.Trade is not null))
        {
            Math.Abs(walk.Trade!.EntryPrice - walk.Trade.StopPrice).ShouldBeGreaterThan(0, $"seed {walk.Day.Seed} {walk.Trade.Side}");
        }

        walks.Count(w => w.Outcome.NoTrade == NoTradeReason.EntryThroughStop).ShouldBeGreaterThanOrEqualTo(10, "the guard is exercised by the seeded days");
    }

    [Fact]
    public void No_figure_of_a_seeded_trade_is_a_negative_zero()
    {
        foreach (var rules in new[] { FillRules.Default, None })
        {
            foreach (var trade in WalkProperties.WalkAll(SeededDays.All, rules).Select(w => w.Trade).OfType<SimTrade>())
            {
                double[] figures = [trade.RExact, trade.MaeR, trade.MfeR, trade.RFilled, trade.SlippageR];

                figures.ShouldAllBe(v => !(v == 0 && double.IsNegative(v)), $"{trade.Symbol} {trade.ExitEpochSeconds}");
            }
        }
    }

    [Fact]
    public void R_and_the_excursions_of_every_seeded_trade_equal_the_plain_reference_loop_which_knows_empty_minutes_the_limit_fill_minute_and_the_gapped_target()
    {
        var compared = 0;
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var bars = walk.Day.Bars;
            var candidate = walk.Walk.Candidate;
            var fillIndex = WalkProperties.IndexOf(bars, walk.Trade!.EntryEpochSeconds, candidate.DecisionBarIndex + 1);
            var settled = WalkProperties.SettledAt(candidate, bars, walk.Outcome);

            FillProperties.AssertExcursions(walk.Trade, bars, walk.Outcome, settled, fillIndex, $"seed {walk.Day.Seed} {walk.Trade.Side} {walk.Trade.ExitReason}");
            compared++;
        }

        compared.ShouldBeGreaterThanOrEqualTo(1_500);
    }

    [Fact]
    public void The_check_is_not_vacuous_it_names_a_square_off_moved_a_minute_a_price_off_the_grid_and_a_fill_before_the_decision()
    {
        var walk = WalkProperties.WalkAll(SeededDays.All, FillRules.Default).First(w => w.Trade is { ExitReason: ExitReason.SquareOff } && w.Trade.EntryFill == EntryFill.AtOpen && !w.Outcome.EntryAfterMissingMinutes);
        var trade = walk.Trade!;
        var rules = FillRules.Default;

        FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate, walk.Outcome, rules).ShouldBeEmpty();
        FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate, walk.Outcome with { Trade = trade with { ExitEpochSeconds = trade.ExitEpochSeconds + 60 } }, rules)
            .ShouldContain(v => v.Contains("square-off", StringComparison.Ordinal) || v.Contains("exit minute", StringComparison.Ordinal));
        FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate, walk.Outcome with { Trade = trade with { EntryFilledPrice = trade.EntryFilledPrice + (double)walk.Day.Tick / 3 } }, rules)
            .ShouldContain(v => v.Contains("multiple of the tick", StringComparison.Ordinal));
        FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate with { DecisionEpochSeconds = trade.EntryEpochSeconds + 60 }, walk.Outcome, rules)
            .ShouldContain(v => v.Contains("at or after the decision instant", StringComparison.Ordinal));
        FillTimingCheck.Violations(walk.Day, walk.Walk.Candidate, walk.Outcome with { Trade = trade with { EntryPrice = trade.StopPrice } }, rules)
            .ShouldContain(v => v.Contains("risk is above zero", StringComparison.Ordinal));
    }
}
