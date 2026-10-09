namespace XDrishti.DhanTrial.Tests;

public class TimestampAnalyzerTests
{
    private static List<Bar> Days(EpochBase epoch, BarLabel label, int days = 5)
    {
        var bars = new List<Bar>();
        for (var i = 0; i < days; i++)
        {
            bars.AddRange(TestData.Session(TestData.Monday.AddDays(i), epoch, label));
        }

        return bars;
    }

    [Theory]
    [InlineData("TrueUtc", "Start")]
    [InlineData("IstAsUtc", "Start")]
    [InlineData("TrueUtc", "End")]
    [InlineData("IstAsUtc", "End")]
    public void Epoch_base_and_bar_label_are_detected_from_full_sessions(string epochName, string labelName)
    {
        var epoch = Enum.Parse<EpochBase>(epochName);
        var label = Enum.Parse<BarLabel>(labelName);
        var result = TimestampAnalyzer.Classify(Days(epoch, label));

        result.Epoch.ShouldBe(epoch);
        result.Label.ShouldBe(label);
        result.DaysChecked.ShouldBe(5);
    }

    [Fact]
    public void Too_little_data_is_reported_unknown_rather_than_guessed()
    {
        var result = TimestampAnalyzer.Classify(TestData.Session(TestData.Monday).Take(50).ToList());

        result.Epoch.ShouldBe(EpochBase.Unknown);
    }

    [Theory]
    [InlineData(1_760_000_000L - (1_760_000_000L % 86400), 0)]
    public void A_midnight_utc_daily_epoch_is_the_calendar_date_itself(long epoch, int offsetDays)
    {
        var expected = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime).AddDays(offsetDays);

        TimestampAnalyzer.DailyDate(epoch).ShouldBe(expected);
    }

    [Fact]
    public void A_real_instant_of_ist_midnight_maps_to_the_ist_date()
    {
        var istMidnight = new DateTimeOffset(2026, 10, 7, 0, 0, 0, IstTime.Offset).ToUnixTimeSeconds();

        TimestampAnalyzer.DailyDate(istMidnight).ShouldBe(new DateOnly(2026, 10, 7));
    }
}

public class BarAnalyzerTests
{
    private static readonly TimestampSemantics Utc = new(EpochBase.TrueUtc, BarLabel.Start, 5, "test");

    [Fact]
    public void A_complete_session_is_one_hundred_percent_complete()
    {
        var bars = TestData.Session(TestData.Monday);
        var expected = BarAnalyzer.ExpectedByDay([bars], Utc);

        var q = BarAnalyzer.Analyze("X", bars, Utc, expected);

        q.TotalBars.ShouldBe(375);
        q.Completeness.ShouldBe(100);
        q.MissingDays.ShouldBeEmpty();
        q.ShortDays.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_minutes_and_whole_days_are_counted_against_the_best_covered_series()
    {
        var full = TestData.Session(TestData.Monday).Concat(TestData.Session(TestData.Monday.AddDays(1))).ToList();
        var thin = TestData.Session(TestData.Monday, skip: Enumerable.Range(600, 10));
        var expected = BarAnalyzer.ExpectedByDay([full, thin], Utc);

        var q = BarAnalyzer.Analyze("THIN", thin, Utc, expected);

        q.MissingMinutes.ShouldBe(10 + 375);
        q.MissingDays.ShouldBe([TestData.Monday.AddDays(1)]);
        q.ShortDays.Count.ShouldBe(2);
    }

    [Fact]
    public void A_symbol_is_only_judged_on_its_own_window_not_on_days_only_a_longer_series_has()
    {
        var older = TestData.Session(TestData.Monday.AddDays(-14));
        var shared = TestData.Session(TestData.Monday);
        var longSeries = older.Concat(shared).ToList();
        var expected = BarAnalyzer.ExpectedByDay([longSeries, shared], Utc);

        var judgedOverEverything = BarAnalyzer.Analyze("SHORT", shared, Utc, expected);
        var judgedOnOwnWindow = BarAnalyzer.Analyze("SHORT", shared, Utc, expected, TestData.Monday, TestData.Monday);

        judgedOverEverything.MissingMinutes.ShouldBe(375);
        judgedOnOwnWindow.MissingMinutes.ShouldBe(0);
        judgedOnOwnWindow.Completeness.ShouldBe(100);
    }

    [Fact]
    public void A_half_day_that_every_series_shares_is_not_a_gap()
    {
        var half = TestData.Session(TestData.Monday).Take(200).ToList();
        var expected = BarAnalyzer.ExpectedByDay([half], Utc);

        BarAnalyzer.Analyze("X", half, Utc, expected).MissingMinutes.ShouldBe(0);
    }

    [Fact]
    public void Zero_volume_duplicates_out_of_order_out_of_session_and_bad_prices_are_flagged()
    {
        var bars = TestData.Session(TestData.Monday);
        bars[10] = bars[10] with { Volume = 0 };
        bars.Add(bars[5]);
        (bars[20], bars[21]) = (bars[21], bars[20]);
        bars.Add(new Bar(TestData.Epoch(TestData.Monday, 8 * 60, EpochBase.TrueUtc), 100, 101, 99, 100, 5));
        bars[30] = bars[30] with { High = 50 };
        var expected = BarAnalyzer.ExpectedByDay([bars], Utc);

        var q = BarAnalyzer.Analyze("X", bars, Utc, expected);

        q.ZeroVolume.ShouldBe(1);
        q.Duplicates.ShouldBe(1);
        q.OutOfOrder.ShouldBeGreaterThan(0);
        q.OutOfSession.ShouldBe(1);
        q.BadPrice.ShouldBe(1);
    }

    [Fact]
    public void End_labelled_bars_are_shifted_to_start_labels_before_counting()
    {
        var endLabelled = new TimestampSemantics(EpochBase.TrueUtc, BarLabel.End, 5, "test");
        var bars = TestData.Session(TestData.Monday, label: BarLabel.End);
        var expected = BarAnalyzer.ExpectedByDay([bars], endLabelled);

        BarAnalyzer.Analyze("X", bars, endLabelled, expected).Completeness.ShouldBe(100);
    }
}

public class DailyCrossCheckTests
{
    private static readonly TimestampSemantics Utc = new(EpochBase.TrueUtc, BarLabel.Start, 5, "test");

    private static Bar DailyFor(List<Bar> minutes, long volume, double? close = null)
    {
        var date = TestData.Monday;
        var epoch = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        return new Bar(epoch, minutes[0].Open, minutes.Max(b => b.High), minutes.Min(b => b.Low), close ?? minutes[^1].Close, volume);
    }

    [Fact]
    public void A_daily_candle_equal_to_the_derived_day_has_no_mismatches()
    {
        var minutes = TestData.Session(TestData.Monday);

        var result = DailyCrossCheck.Compare([DailyFor(minutes, minutes.Sum(b => b.Volume))], minutes, Utc);

        result.DaysCompared.ShouldBe(1);
        (result.OpenMismatch + result.HighMismatch + result.LowMismatch + result.CloseMismatch + result.VolumeMismatch).ShouldBe(0);
    }

    [Fact]
    public void Different_close_and_volume_are_counted_as_mismatches()
    {
        var minutes = TestData.Session(TestData.Monday);

        var result = DailyCrossCheck.Compare([DailyFor(minutes, minutes.Sum(b => b.Volume) * 2, close: 250)], minutes, Utc);

        result.CloseMismatch.ShouldBe(1);
        result.VolumeMismatch.ShouldBe(1);
        result.WorstVolumeRatio.ShouldBe(0.5, 0.0001);
    }

    [Fact]
    public void Index_candles_with_zero_volume_are_not_compared_on_volume()
    {
        var minutes = TestData.Session(TestData.Monday, volume: 0);

        DailyCrossCheck.Compare([DailyFor(minutes, 0)], minutes, Utc).VolumeMismatch.ShouldBe(0);
    }

    [Fact]
    public void A_halving_of_price_between_days_is_flagged_as_a_possible_unadjusted_split()
    {
        var day = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var daily = new List<Bar> { new(day, 100, 101, 99, 100, 1), new(day + 86400, 50, 51, 49, 50, 1), new(day + (2 * 86400), 50, 51, 49, 51, 1) };

        DailyCrossCheck.PriceDiscontinuities(daily).ShouldBe([new DateOnly(2026, 10, 6)]);
    }
}
