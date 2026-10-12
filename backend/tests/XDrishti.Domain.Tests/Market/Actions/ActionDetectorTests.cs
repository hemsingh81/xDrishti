using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Tests.Market.Actions;

/// <summary>Detection of suspected splits and bonus issues in a daily series: price falls to a common factor, on a jump in volume, and stays there.</summary>
public sealed class ActionDetectorTests
{
    private const int EventDay = 40;

    private static readonly DateOnly All = new(2000, 1, 1);

    /// <summary>A calm series of <paramref name="days"/> days in which day <see cref="EventDay"/> opens at <paramref name="ratio"/> of the previous close on <paramref name="volumeMultiple"/> times the usual volume; the prices after it follow the new level.</summary>
    private static List<DailyBar> WithGap(double ratio, double volumeMultiple, int days = 60, Func<int, DailyBar, DailyBar>? after = null, Func<DailyBar, DailyBar>? eventDay = null, Func<int, DailyBar, DailyBar>? before = null)
    {
        var level = 1.0;
        return Series(days, (i, bar) =>
        {
            if (i == EventDay)
            {
                level = ratio;
                return eventDay?.Invoke(bar) ?? Gap(bar, bar.Open, ratio, volumeMultiple);
            }

            if (i > EventDay)
            {
                var scaled = bar with { Open = bar.Open * level, High = bar.High * level, Low = bar.Low * level, Close = bar.Close * level };
                return after?.Invoke(i, scaled) ?? scaled;
            }

            return before?.Invoke(i, bar) ?? bar;
        });
    }

    private static List<DailyBar> Series(int days, Func<int, DailyBar, DailyBar> shape)
    {
        var bars = new List<DailyBar>();
        var date = new DateOnly(2026, 1, 5);
        var close = 1000.0;
        for (var i = 0; i < days; i++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            // The unscaled series drifts around 1000 by at most 0.4 % a day; day i+1 opens at day i's raw close (the scaling is applied by the shape).
            var open = close;
            close = open * (1 + (((i * 7) % 9) - 4) / 1000.0);
            var raw = new DailyBar(date, open, Math.Max(open, close) * 1.002, Math.Min(open, close) * 0.998, close, 100_000 + (((i * 13) % 11) - 5) * 1000L);
            var shaped = shape(i, raw);
            bars.Add(shaped);
            date = date.AddDays(1);
        }

        return bars;
    }

    private static DailyBar Gap(DailyBar bar, double previousClose, double ratio, double volumeMultiple)
    {
        var level = previousClose * ratio;
        return bar with { Open = level, High = level * 1.004, Low = level * 0.996, Close = level * 1.001, Volume = (long)(100_000 * volumeMultiple) };
    }

    [Fact]
    public void A_one_for_five_split_is_found_on_its_ex_date_with_a_note_that_names_the_bonus_reading()
    {
        var bars = WithGap(0.2, 5.0);

        var found = ActionDetector.Detect(bars, All).ShouldHaveSingleItem();

        (found.ExDate, found.Kind, found.Numerator, found.Denominator).ShouldBe((bars[EventDay].Date, CorporateActionKind.Split, 1, 5));
        found.Note.ShouldContain("Open was 0.200 of the previous close");
        found.Note.ShouldContain("5.0x the usual volume");
        found.Note.ShouldContain("Could also be bonus 4:1.");
    }

    [Fact]
    public void A_one_for_one_bonus_halves_the_price_and_is_suggested_as_a_split_one_to_two_naming_the_bonus()
    {
        var found = ActionDetector.Detect(WithGap(0.5, 2.0), All).ShouldHaveSingleItem();

        (found.Kind, found.Numerator, found.Denominator).ShouldBe((CorporateActionKind.Split, 1, 2));
        found.Note.ShouldContain("Could also be bonus 1:1.");
    }

    [Theory]
    [InlineData(1.0 / 3, CorporateActionKind.Split, 1, 3)]
    [InlineData(0.25, CorporateActionKind.Split, 1, 4)]
    [InlineData(0.1, CorporateActionKind.Split, 1, 10)]
    [InlineData(0.05, CorporateActionKind.Split, 1, 20)]
    [InlineData(2.0 / 3, CorporateActionKind.Bonus, 1, 2)]
    [InlineData(0.75, CorporateActionKind.Bonus, 1, 3)]
    public void Every_common_factor_maps_to_one_reading(double ratio, CorporateActionKind kind, int numerator, int denominator)
    {
        var found = ActionDetector.Detect(WithGap(ratio, 3.0), All).ShouldHaveSingleItem();

        (found.Kind, found.Numerator, found.Denominator).ShouldBe((kind, numerator, denominator));
    }

    [Theory]
    [InlineData(0.2 * 1.024, true)]
    [InlineData(0.2 * 0.976, true)]
    [InlineData(0.2 * 1.03, false)]
    [InlineData(0.2 * 0.97, false)]
    public void The_ratio_must_be_within_two_and_a_half_percent_of_the_factor(double ratio, bool found)
    {
        ActionDetector.Detect(WithGap(ratio, 5.0), All).Count.ShouldBe(found ? 1 : 0);
    }

    [Fact]
    public void A_gap_that_comes_back_within_two_sessions_is_a_false_positive()
    {
        var bars = WithGap(0.5, 4.0, after: (i, bar) => i == EventDay + 2 ? bar with { Open = 995, High = 1000, Low = 990, Close = 998 } : bar);

        ActionDetector.Detect(bars, All).ShouldBeEmpty();
    }

    [Fact]
    public void A_gap_that_comes_back_on_the_same_day_is_not_a_split_either()
    {
        var bars = Series(60, (i, bar) => i == EventDay ? bar with { Open = bar.Open * 0.5, Low = bar.Open * 0.49, Close = bar.Open * 0.99, Volume = 400_000 } : bar);

        ActionDetector.Detect(bars, All).ShouldBeEmpty();
    }

    [Fact]
    public void A_reversal_after_more_than_three_sessions_does_not_count()
    {
        var bars = WithGap(0.5, 4.0, after: (i, bar) => i == EventDay + 4 ? bar with { Close = 1000, High = 1000 } : bar);

        ActionDetector.Detect(bars, All).Count.ShouldBe(1);
    }

    [Fact]
    public void A_gap_down_on_ordinary_volume_is_a_crash_not_a_split()
    {
        ActionDetector.Detect(WithGap(0.5, 1.0), All).ShouldBeEmpty();
    }

    [Fact]
    public void A_gap_on_less_than_one_and_a_half_times_the_volume_is_not_enough_and_exactly_that_is()
    {
        ActionDetector.Detect(WithGap(0.2, 1.4), All).ShouldBeEmpty();
        ActionDetector.Detect(WithGap(0.2, 1.6), All).Count.ShouldBe(1);
    }

    [Fact]
    public void When_only_the_close_matches_the_factor_it_is_still_found()
    {
        var bars = WithGap(0.2, 5.0, eventDay: bar => bar with { Open = bar.Open * 0.4, High = bar.Open * 0.4, Low = bar.Open * 0.195, Close = bar.Open * 0.2, Volume = 500_000 });

        var found = ActionDetector.Detect(bars, All).ShouldHaveSingleItem();

        found.Note.ShouldStartWith("Close was 0.200");
    }

    [Fact]
    public void A_price_rise_is_not_looked_for()
    {
        ActionDetector.Detect(WithGap(5.0, 5.0), All).ShouldBeEmpty();
    }

    [Fact]
    public void Too_little_history_to_know_the_usual_volume_finds_nothing()
    {
        var bars = Series(EventDay + 1, (i, bar) => i == 6 ? Gap(bar, bar.Open, 0.2, 5.0) : bar);

        ActionDetector.Detect(bars, All).ShouldBeEmpty();
    }

    [Fact]
    public void A_candidate_on_the_newest_day_is_judged_on_the_sessions_there_are()
    {
        var bars = WithGap(0.2, 5.0, days: EventDay + 1);

        ActionDetector.Detect(bars, All).ShouldHaveSingleItem().ExDate.ShouldBe(bars[EventDay].Date);
    }

    [Fact]
    public void Only_days_from_the_given_date_on_are_candidates_but_earlier_days_give_the_context()
    {
        var bars = WithGap(0.2, 5.0);

        ActionDetector.Detect(bars, bars[EventDay].Date).Count.ShouldBe(1);
        ActionDetector.Detect(bars, bars[EventDay].Date.AddDays(1)).ShouldBeEmpty();
        ActionDetector.Detect(bars[(EventDay - 25)..], bars[EventDay].Date).Count.ShouldBe(1);
    }

    [Fact]
    public void Running_it_again_gives_the_same_candidates()
    {
        var bars = WithGap(0.25, 4.0);

        ActionDetector.Detect(bars, All).ShouldBe(ActionDetector.Detect(bars, All));
    }

    [Fact]
    public void A_series_without_a_zero_volume_median_still_works_and_zero_volume_days_are_never_candidates()
    {
        var quiet = WithGap(0.2, 5.0, before: (i, bar) => i is >= 10 and < 40 ? bar with { Volume = 0 } : bar);
        var empty = WithGap(0.2, 0);

        ActionDetector.Detect(quiet, All).Count.ShouldBe(1);
        ActionDetector.Detect(empty, All).ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_is_found_in_a_calm_series_or_an_empty_one()
    {
        ActionDetector.Detect(Series(120, (_, bar) => bar), All).ShouldBeEmpty();
        ActionDetector.Detect([], All).ShouldBeEmpty();
    }
}
