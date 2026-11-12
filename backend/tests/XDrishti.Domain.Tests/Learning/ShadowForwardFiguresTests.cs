using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10e part 10e-1, spec D3: the forward figures of a model on its graded shadow candidates.</summary>
public sealed class ShadowForwardFiguresTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    /// <summary>n candidates; the first half (by probability) earns <paramref name="top"/> R and the rest <paramref name="bottom"/> R, spread over <paramref name="days"/> days.</summary>
    private static List<ScoredOutcome> Outcomes(int n, double top, double bottom, int days = 35)
    {
        var rows = new List<ScoredOutcome>();
        for (var i = 0; i < n; i++)
        {
            var upper = i < n / 2;
            rows.Add(new ScoredOutcome(Start.AddDays(i * days / Math.Max(1, n - 1)), "k" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), 0.9 - (i * 0.01), i == n / 2 && n % 2 == 1 ? 9 : upper ? top : bottom));
        }

        return rows;
    }

    [Fact]
    public void With_no_outcome_nothing_is_available()
    {
        ShadowForwardFigures.Compute([]).ShouldBeNull();
    }

    [Fact]
    public void Thirty_four_candidates_over_five_weeks_give_the_top_half_minus_the_bottom_half()
    {
        var figures = ShadowForwardFigures.Compute(Outcomes(34, 0.21, 0.05))!;

        figures.GradedCandidates.ShouldBe(34);
        figures.Weeks.ShouldBe(5.0);
        figures.TopMinusBottomR!.Value.ShouldBe(0.16, 1e-9);
    }

    [Fact]
    public void With_an_odd_count_the_middle_candidate_is_left_out()
    {
        var figures = ShadowForwardFigures.Compute(Outcomes(35, 0.21, 0.05))!;

        figures.GradedCandidates.ShouldBe(35);
        figures.TopMinusBottomR!.Value.ShouldBe(0.16, 1e-9); // the middle one earns 9 R and would move the figure if it were counted
    }

    [Fact]
    public void Equal_probabilities_are_ranked_by_trade_key_in_ordinal_order()
    {
        var rows = new List<ScoredOutcome> { new(Start, "b", 0.5, 1), new(Start, "a", 0.5, 3), new(Start.AddDays(7), "c", 0.5, 0), new(Start.AddDays(7), "d", 0.5, -2) };

        var figures = ShadowForwardFigures.Compute(rows)!;

        figures.TopMinusBottomR!.Value.ShouldBe(((3 + 1) / 2.0) - ((0 + -2) / 2.0), 1e-9);
    }

    [Fact]
    public void A_single_candidate_has_no_spread()
    {
        var figures = ShadowForwardFigures.Compute([new ScoredOutcome(Start, "a", 0.5, 1)])!;

        figures.TopMinusBottomR.ShouldBeNull();
        figures.Weeks.ShouldBe(0);
    }

    [Fact]
    public void The_calibration_error_reads_only_the_newest_hundred()
    {
        // 100 old, badly calibrated outcomes (always 0.9, always a loss) then 100 perfectly calibrated ones (0.5, half win)
        var rows = Enumerable.Range(0, 100).Select(i => new ScoredOutcome(Start, "o" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), 0.9, -1)).ToList();
        rows.AddRange(Enumerable.Range(0, 100).Select(i => new ScoredOutcome(Start.AddDays(10), "n" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), 0.5, i % 2 == 0 ? 1 : -1)));

        var figures = ShadowForwardFigures.Compute(rows)!;

        figures.RollingEcePp!.Value.ShouldBe(0, 1e-9);
    }
}
