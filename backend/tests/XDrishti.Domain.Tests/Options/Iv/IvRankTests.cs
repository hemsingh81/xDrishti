using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Iv;
using static XDrishti.Domain.Tests.Options.Iv.IvKit;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>P14-11 criterion 1: the hand values of the rank and the percentile (252 values 10 to 261 percent, the previous session at 136 percent), and the window rules.</summary>
public sealed class IvRankTests
{
    private static List<double> Hand() => [.. Enumerable.Range(10, 252).Select(v => v / 100.0)];

    [Fact]
    public void The_rank_and_the_percentile_of_the_hand_values()
    {
        var result = IvRank.Of(Hand(), 1.36);

        result.IsKnown.ShouldBeTrue();
        result.Rank.ShouldBe((1.36 - 0.10) / (2.61 - 0.10), 1e-12);
        result.Rank.ShouldBe(0.502, 1e-3);
        result.Percentile.ShouldBe(126.0 / 252.0, 1e-12);
        result.Percentile.ShouldBe(0.5, 1e-12);
    }

    [Fact]
    public void Fewer_than_two_hundred_values_is_short_history_and_two_hundred_is_enough()
    {
        IvRank.Of([.. Hand().Take(199)], 1.0).Reason.ShouldBe(FeatureReasons.ShortHistory);
        IvRank.Of([.. Hand().Take(200)], 1.0).IsKnown.ShouldBeTrue();
        double.IsNaN(IvRank.Of([.. Hand().Take(199)], 1.0).Rank).ShouldBeTrue("an unknown is NaN, never zero");
    }

    [Fact]
    public void A_flat_window_has_no_rank()
    {
        var flat = Enumerable.Repeat(0.2, 252).ToList();

        var result = IvRank.Of(flat, 0.2);

        result.Reason.ShouldBe(IvReasons.FlatHistory);
        double.IsNaN(result.Percentile).ShouldBeTrue();
    }

    [Fact]
    public void The_series_rank_uses_the_previous_session_as_x_and_the_252_sessions_before_the_trade_date_only()
    {
        // session i back from the trade date has the value i/100 (1 percent for the previous session, 252 percent for the oldest); the trade date's own value and later ones are huge and must not matter
        var series = new Dictionary<DateOnly, double>(Series(Wed, 252, i => i / 100.0)) { [Wed] = 99.0, [Wed.AddDays(1)] = 99.0 };

        var result = IvRank.OfSeries(series, Wed, Calendar);

        result.IsKnown.ShouldBeTrue();
        result.Rank.ShouldBe(0.0, 1e-12, "the previous session holds the lowest value of the window");
        result.Percentile.ShouldBe(0.0, 1e-12);
    }

    [Fact]
    public void A_missing_value_for_the_previous_session_is_short_history_not_a_neighbours_value()
    {
        var series = new Dictionary<DateOnly, double>(Series(Wed, 252, i => i / 100.0));
        series.Remove(new DateOnly(2026, 7, 28));

        IvRank.OfSeries(series, Wed, Calendar).Reason.ShouldBe(FeatureReasons.ShortHistory);
    }

    [Fact]
    public void Values_older_than_the_252_sessions_are_not_part_of_the_window()
    {
        var series = new Dictionary<DateOnly, double>(Series(Wed, 300, i => i <= 252 ? 0.5 + (i / 1000.0) : 50.0));

        var result = IvRank.OfSeries(series, Wed, Calendar);

        result.IsKnown.ShouldBeTrue();
        result.Rank.ShouldBe(0.0, 1e-12, "the 48 older and huge values are outside the window");
    }
}
