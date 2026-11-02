using System.Text.Json;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Protocol F of P15-10: criterion 1 (tail alone is a KEEP, 60 % removal is a DROP), criterion 2 (the unavailable reasons and the 50 % rule), criterion 3 (thresholds), criterion 7 (determinism), the render rule.</summary>
public sealed class ProtocolFTests
{
    private static readonly ProtocolFOptions Fast = new() { Draws = 500 };

    private static ProtocolOutcome Run(List<FilterTrade> trades, int seasonsWithData = 12, int variants = 3, int days = 2) =>
        ProtocolF.Evaluate(days, variants, seasonsWithData, trades, Fast).Value.Outcome;

    // 8 seasons: 25 skipped, 100 taken per season; a gap-through tail of 20 % in the skipped and 5 % in the taken; equal means so only the tail test can pass.
    private static List<FilterTrade> PlantedTail() => FilterKit.Population(8, 25, 100, 5, 5, equalMeans: true);

    [Fact]
    public void A_planted_gap_through_tail_in_the_skipped_set_is_a_keep_on_the_tail_test_alone()
    {
        var result = ProtocolF.Evaluate(2, 3, 12, PlantedTail(), Fast).Value;

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep);
        result.Basis.ShouldBe("tail");
        result.Outcome.Failed.ShouldBeEmpty();
        result.Outcome.Missing.ShouldBeEmpty();
        result.Outcome.FoldsEvaluable.ShouldBe(8);
    }

    [Fact]
    public void A_planted_removal_of_sixty_percent_is_a_drop_with_the_reason()
    {
        // 200 skipped + 400 unavailable of 1000 = 60 %.
        var trades = FilterKit.Population(8, 25, 50, 5, 5, equalMeans: true, noUpcoming: 50);

        var outcome = Run(trades);

        outcome.Verdict.ShouldBe(ExtensionVerdict.Drop);
        outcome.Failed.ShouldContain(f => f.Contains("removes 60.0 %", StringComparison.Ordinal) && f.Contains("skipped 200 plus unavailable 400 of 1000", StringComparison.Ordinal));
    }

    [Fact]
    public void Exactly_half_removed_is_allowed()
    {
        // 200 skipped + 300 unavailable of 1000 = 50 %.
        var trades = FilterKit.Population(8, 25, 75, 8, 4, equalMeans: true, noUpcoming: 25);

        Run(trades).Verdict.ShouldBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void The_report_prints_each_unavailable_reason_and_the_share_lost_to_no_upcoming_entry_and_counts_them_as_removed()
    {
        // Hand table over 8 seasons, 125 trades each = 1000: skipped 15 (120), taken 50 (400), unavailable 60 (480): no_upcoming 45 (360), no_entry 10 (80), only_cancelled 5 (40).
        var trades = FilterKit.Population(8, 15, 50, 3, 2, equalMeans: true, noUpcoming: 45, noEntry: 10, onlyCancelled: 5);

        var outcome = Run(trades);
        using var estimates = JsonDocument.Parse(outcome.EstimatesJson);
        var root = estimates.RootElement;

        root.GetProperty("trades").GetInt32().ShouldBe(1000);
        root.GetProperty("skipped_share").GetDouble().ShouldBe(0.12, 1e-9);
        root.GetProperty("taken_share").GetDouble().ShouldBe(0.4, 1e-9);
        root.GetProperty("unavailable_share").GetDouble().ShouldBe(0.48, 1e-9);
        root.GetProperty("share_lost_to_no_upcoming_entry").GetDouble().ShouldBe(0.36, 1e-9);
        root.GetProperty("unavailable_by_reason").GetProperty("no_entry").GetProperty("trades").GetInt32().ShouldBe(80);
        root.GetProperty("unavailable_by_reason").GetProperty("only_cancelled").GetProperty("share").GetDouble().ShouldBe(0.04, 1e-9);
        root.GetProperty("removed_share").GetDouble().ShouldBe(0.6, 1e-9);
        outcome.Verdict.ShouldBe(ExtensionVerdict.Drop);
        outcome.Failed.ShouldContain(f => f.Contains("unavailable 480", StringComparison.Ordinal));
    }

    [Fact]
    public void Fewer_than_one_hundred_skipped_trades_is_not_evaluable_and_names_the_count_with_no_digit_for_a_non_value()
    {
        var trades = FilterKit.Population(8, 7, 100, 1, 5, equalMeans: false);   // 56 skipped

        var outcome = Run(trades);

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(["skipped trades 56 of 100"]);
        using var estimates = JsonDocument.Parse(outcome.EstimatesJson);
        foreach (var name in new[] { "mean_gap_r", "mean_gap_lower", "mean_gap_upper", "mean_gap_required", "basis" })
        {
            estimates.RootElement.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.String, name);
            estimates.RootElement.GetProperty(name).GetString().ShouldBe("unavailable", name);
        }

        estimates.RootElement.GetProperty("skipped").GetInt32().ShouldBe(56);
    }

    [Fact]
    public void Seasons_not_more_than_the_reserved_four_are_not_evaluable()
    {
        var outcome = Run(PlantedTail(), seasonsWithData: 4);

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldContain("seasons 4 of 5 (the last 4 are reserved as the hold-out)");
    }

    [Fact]
    public void Fewer_than_four_judged_seasons_is_not_evaluable_and_names_the_count()
    {
        var outcome = Run(FilterKit.Population(3, 40, 100, 8, 5, equalMeans: true));

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(["judged seasons 3 of 4"]);
    }

    [Fact]
    public void A_tail_ratio_that_holds_for_the_point_shares_but_not_for_the_wilson_intervals_is_not_a_keep()
    {
        // 104 skipped with 2 tails (1.9 %) against 400 taken with 1 tail (0.25 %): 7.7 times as many by the point shares, but the intervals overlap.
        var trades = FilterKit.Population(8, 13, 50, 0, 0, equalMeans: false, skippedOther: 0.5);
        var swap = trades.Where(t => t.Fate == FilterFate.Skipped).Take(2).Select(t => t.Key).Concat(trades.Where(t => t.Fate == FilterFate.Taken).Take(1).Select(t => t.Key)).ToHashSet();
        var planted = trades.Select(t => swap.Contains(t.Key) ? t with { R = -2.0 } : t).ToList();

        var outcome = Run(planted);

        outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
        outcome.Failed.ShouldContain(f => f.Contains("Wilson", StringComparison.Ordinal) || f.Contains("upper bound of the taken", StringComparison.Ordinal));
    }

    [Fact]
    public void No_taken_trades_is_named()
    {
        var outcome = Run(FilterKit.Population(8, 25, 1, 5, 0, equalMeans: false).Where(t => t.Fate != FilterFate.Taken).ToList());

        outcome.Missing.ShouldBe(["taken trades 0"]);
    }

    [Fact]
    public void A_lower_mean_alone_is_a_keep_when_its_lower_bound_clears_the_factor_widened_margin()
    {
        // Skipped mean 0.5 - ..., taken mean far higher, no tails: skipped R = -0.5, taken R = 0.5, constant: gap 1.0.
        var trades = FilterKit.Population(8, 25, 100, 0, 0, equalMeans: false, skippedOther: -0.5);

        var result = ProtocolF.Evaluate(2, 3, 12, trades, Fast).Value;

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep);
        result.Basis.ShouldBe("mean");
    }

    [Fact]
    public void No_difference_at_all_is_inconclusive_or_drop_never_a_keep()
    {
        var trades = FilterKit.Population(8, 25, 100, 0, 0, equalMeans: false, skippedOther: 0.5);

        Run(trades).Verdict.ShouldNotBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void The_margin_is_widened_by_the_ledger_count()
    {
        var outcome1 = Run(PlantedTail(), variants: 1);
        var outcome16 = Run(PlantedTail(), variants: 16);

        using var a = JsonDocument.Parse(outcome1.EstimatesJson);
        using var b = JsonDocument.Parse(outcome16.EstimatesJson);
        a.RootElement.GetProperty("mean_gap_required").GetDouble().ShouldBe(0.03, 1e-9);
        b.RootElement.GetProperty("mean_gap_required").GetDouble().ShouldBe(0.03 * 2, 1e-9);
    }

    [Fact]
    public void The_order_of_the_trades_does_not_change_the_record()
    {
        var trades = PlantedTail();
        var shuffled = trades.OrderByDescending(t => t.Key, StringComparer.Ordinal).ToList();

        var a = Run(trades);
        var b = Run(shuffled);

        b.EstimatesJson.ShouldBe(a.EstimatesJson);
        b.EvidenceJson.ShouldBe(a.EvidenceJson);
    }

    [Fact]
    public void Split_reserves_the_last_four_seasons_and_with_no_more_than_four_everything_is_reserved()
    {
        var trades = FilterKit.Population(6, 2, 2, 0, 0, equalMeans: false);
        var (development, holdOut, seasons) = ProtocolF.Split(trades);

        seasons.ShouldBe(6);
        development.Select(t => Season.Of(t.Date)).Distinct().Count().ShouldBe(2);
        holdOut.Select(t => Season.Of(t.Date)).Distinct().Count().ShouldBe(4);
        ProtocolF.Split(FilterKit.Population(4, 2, 2, 0, 0, equalMeans: false)).Development.ShouldBeEmpty();
    }

    [Fact]
    public void The_hold_out_confirms_a_tail_that_holds_and_does_not_confirm_one_that_does_not()
    {
        var holds = FilterKit.Population(4, 25, 100, 5, 5, equalMeans: true);
        var fails = FilterKit.Population(4, 25, 100, 0, 5, equalMeans: true);

        ProtocolF.HoldOut(2, holds, "tail", Fast).Value.Verdict.ShouldBe(ExtensionVerdict.Keep);
        ProtocolF.HoldOut(2, fails, "tail", Fast).Value.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ProtocolF.HoldOut(2, FilterKit.Population(4, 3, 100, 1, 5, equalMeans: true), "tail", Fast).Value.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        ProtocolF.HoldOut(2, FilterKit.Population(4, 25, 5, 5, 0, equalMeans: false), "tail", Fast).Value.Missing.ShouldBe(["hold-out taken trades 20 of 30"]);
    }

    [Fact]
    public void The_mean_hold_out_needs_a_lower_bound_above_zero()
    {
        var gap = FilterKit.Population(4, 25, 100, 0, 0, equalMeans: false, skippedOther: -0.5);
        var none = FilterKit.Population(4, 25, 100, 0, 0, equalMeans: false, skippedOther: 0.5);

        ProtocolF.HoldOut(2, gap, "mean", Fast).Value.Verdict.ShouldBe(ExtensionVerdict.Keep);
        ProtocolF.HoldOut(2, none, "mean", Fast).Value.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void A_window_outside_one_to_three_is_refused(int days)
    {
        ProtocolF.Evaluate(days, 3, 12, PlantedTail(), Fast).IsFailure.ShouldBeTrue();
    }
}
