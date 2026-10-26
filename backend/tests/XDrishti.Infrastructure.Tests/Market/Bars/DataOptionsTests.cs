using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Market.Bars;

namespace XDrishti.Infrastructure.Tests.Market.Bars;

public sealed class DataOptionsTests
{
    [Fact]
    public void The_defaults_are_the_documented_ones_and_valid()
    {
        var options = new DataOptions();

        (options.CsvFolder, options.Dhan.InitialDays, options.Dhan.RequestsPerSecond).ShouldBe(("/data/csv", 365, 4));
        DataOptions.IsValid(options).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3651, 4)]
    [InlineData(365, 0)]
    [InlineData(365, 6)]
    public void Settings_outside_their_range_are_refused(int initialDays, int requestsPerSecond)
    {
        var options = new DataOptions { Dhan = new DhanDataOptions { InitialDays = initialDays, RequestsPerSecond = requestsPerSecond } };

        DataOptions.IsValid(options).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3650, 5)]
    public void The_limits_themselves_are_fine(int initialDays, int requestsPerSecond)
    {
        DataOptions.IsValid(new DataOptions { Dhan = new DhanDataOptions { InitialDays = initialDays, RequestsPerSecond = requestsPerSecond } }).ShouldBeTrue();
    }

    [Fact]
    public void The_csv_limits_default_to_five_million_rows_a_gibibyte_and_about_ten_years()
    {
        var options = new DataOptions();

        (options.Csv.MaxRowsPerFile, options.Csv.MaxBytesPerFile, options.Csv.MaxSpanDays).ShouldBe((5_000_000, 1L << 30, 3_700));
    }

    [Theory]
    [InlineData(999, 1_048_576, 3_700)]
    [InlineData(100_000_001, 1_048_576, 3_700)]
    [InlineData(5_000_000, 1_048_575, 3_700)]
    [InlineData(5_000_000, 17_179_869_185, 3_700)]
    [InlineData(5_000_000, 1_048_576, 30)]
    [InlineData(5_000_000, 1_048_576, 36_501)]
    public void Csv_limits_outside_their_range_are_refused(int rows, long bytes, int spanDays)
    {
        DataOptions.IsValid(new DataOptions { Csv = new CsvDataOptions { MaxRowsPerFile = rows, MaxBytesPerFile = bytes, MaxSpanDays = spanDays } }).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1_000, 1_048_576, 31)]
    [InlineData(100_000_000, 17_179_869_184, 36_500)]
    public void The_csv_limits_themselves_are_fine(int rows, long bytes, int spanDays)
    {
        DataOptions.IsValid(new DataOptions { Csv = new CsvDataOptions { MaxRowsPerFile = rows, MaxBytesPerFile = bytes, MaxSpanDays = spanDays } }).ShouldBeTrue();
    }

    [Fact]
    public void The_gap_lookback_defaults_to_30_sessions_and_the_end_of_day_run_to_15_50_switched_on()
    {
        var options = new DataOptions();

        (options.Gaps.LookbackSessions, options.Eod.Enabled, options.Eod.TimeIst).ShouldBe((30, true, "15:50"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(251)]
    public void A_gap_lookback_outside_1_to_250_is_refused(int sessions)
    {
        DataOptions.IsValid(new DataOptions { Gaps = new GapDataOptions { LookbackSessions = sessions } }).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    public void The_gap_lookback_limits_themselves_are_fine(int sessions)
    {
        DataOptions.IsValid(new DataOptions { Gaps = new GapDataOptions { LookbackSessions = sessions } }).ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00")]
    [InlineData("15:50")]
    [InlineData("23:59")]
    public void A_time_as_hh_mm_is_a_valid_end_of_day_time(string time)
    {
        DataOptions.IsValid(new DataOptions { Eod = new EodDataOptions { TimeIst = time } }).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("3:50 pm")]
    [InlineData("15:5")]
    [InlineData("24:00")]
    [InlineData("15:60")]
    [InlineData("15.50")]
    [InlineData("15:50:00")]
    public void Anything_else_is_refused_as_an_end_of_day_time(string time)
    {
        DataOptions.IsValid(new DataOptions { Eod = new EodDataOptions { TimeIst = time } }).ShouldBeFalse();
    }

    [Fact]
    public void The_settings_read_the_options_as_the_pipeline_asks_for_them()
    {
        var settings = new DataSettings(Options.Create(new DataOptions { Gaps = new GapDataOptions { LookbackSessions = 12 }, Eod = new EodDataOptions { Enabled = false, TimeIst = "16:05" } }));

        (settings.GapLookbackSessions, settings.EodEnabled, settings.EodTimeIst).ShouldBe((12, false, new TimeOnly(16, 5)));
    }

    [Fact]
    public void The_quality_checks_default_to_5_sessions_a_spike_multiple_of_20_1_percent_missing_and_5_percent_failed()
    {
        var options = new DataOptions().Quality;

        (options.Sessions, options.SpikeMultiple, options.MaxMissingPercent, options.MaxFailedPercent).ShouldBe((5, 20.0, 1.0, 5.0));
        DataOptions.IsValid(new DataOptions()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20, 1, 5)]
    [InlineData(61, 20, 1, 5)]
    [InlineData(5, 1.9, 1, 5)]
    [InlineData(5, 1001, 1, 5)]
    [InlineData(5, 20, -0.1, 5)]
    [InlineData(5, 20, 100.1, 5)]
    [InlineData(5, 20, 1, -1)]
    [InlineData(5, 20, 1, 101)]
    public void Quality_settings_outside_their_ranges_are_refused(int sessions, double spikeMultiple, double maxMissing, double maxFailed)
    {
        DataOptions.IsValid(new DataOptions { Quality = new QualityDataOptions { Sessions = sessions, SpikeMultiple = spikeMultiple, MaxMissingPercent = maxMissing, MaxFailedPercent = maxFailed } }).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, 2, 0, 0)]
    [InlineData(60, 1000, 100, 100)]
    public void The_quality_limits_themselves_are_fine(int sessions, double spikeMultiple, double maxMissing, double maxFailed)
    {
        DataOptions.IsValid(new DataOptions { Quality = new QualityDataOptions { Sessions = sessions, SpikeMultiple = spikeMultiple, MaxMissingPercent = maxMissing, MaxFailedPercent = maxFailed } }).ShouldBeTrue();
    }

    [Fact]
    public void The_settings_read_the_quality_options()
    {
        var settings = new DataSettings(Options.Create(new DataOptions { Quality = new QualityDataOptions { Sessions = 7, SpikeMultiple = 12, MaxMissingPercent = 2.5, MaxFailedPercent = 10 } }));

        (settings.QualitySessions, settings.QualitySpikeMultiple, settings.QualityMaxMissingPercent, settings.QualityMaxFailedPercent).ShouldBe((7, 12.0, 2.5, 10.0));
    }

    [Fact]
    public void The_invalid_message_names_the_quality_settings()
    {
        DataOptions.InvalidMessage.ShouldContain("Data:Quality:Sessions");
    }

    [Fact]
    public void The_index_depth_defaults_to_five_years_and_the_context_job_is_on_with_its_documented_numbers()
    {
        var options = new DataOptions();

        options.Dhan.InitialDaysIndex.ShouldBe(1825);
        (options.Context.Enabled, options.Context.VixGraceSessions, options.Context.MinDayFraction).ShouldBe((true, 5, 0.96));
        DataOptions.IsValid(options).ShouldBeTrue();
    }

    [Theory]
    [InlineData(364)]
    [InlineData(1826)]
    public void An_index_depth_outside_one_to_five_years_is_refused(int days)
    {
        DataOptions.IsValid(new DataOptions { Dhan = new DhanDataOptions { InitialDaysIndex = days } }).ShouldBeFalse();
    }

    [Theory]
    [InlineData(365)]
    [InlineData(1825)]
    public void The_index_depth_limits_themselves_are_fine(int days)
    {
        DataOptions.IsValid(new DataOptions { Dhan = new DhanDataOptions { InitialDaysIndex = days } }).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 0.96)]
    [InlineData(31, 0.96)]
    [InlineData(5, 0.49)]
    [InlineData(5, 1.01)]
    public void Context_settings_outside_their_range_are_refused(int grace, double fraction)
    {
        DataOptions.IsValid(new DataOptions { Context = new ContextDataOptions { VixGraceSessions = grace, MinDayFraction = fraction } }).ShouldBeFalse();
    }
}
