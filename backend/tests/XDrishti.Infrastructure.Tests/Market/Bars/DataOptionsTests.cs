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
}
