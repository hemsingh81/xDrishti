using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Market.Bars;

public sealed class TimeframesTests
{
    [Theory]
    [InlineData(Timeframe.M1, "1m", 1)]
    [InlineData(Timeframe.M5, "5m", 5)]
    [InlineData(Timeframe.M10, "10m", 10)]
    [InlineData(Timeframe.M15, "15m", 15)]
    [InlineData(Timeframe.M30, "30m", 30)]
    [InlineData(Timeframe.H1, "1h", 60)]
    [InlineData(Timeframe.D1, "1D", 0)]
    [InlineData(Timeframe.W1, "1W", 0)]
    [InlineData(Timeframe.Mo1, "1M", 0)]
    public void Each_timeframe_has_its_code_and_length(Timeframe timeframe, string code, int minutes)
    {
        timeframe.ToCode().ShouldBe(code);
        timeframe.Minutes().ShouldBe(minutes);
        Timeframes.TryParse(code, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(timeframe);
    }

    [Theory]
    [InlineData("1d")]
    [InlineData("1w")]
    [InlineData("1H")]
    [InlineData("2h")]
    [InlineData("")]
    [InlineData(null)]
    public void Codes_are_case_sensitive_because_a_minute_and_a_month_differ_only_by_case(string? code)
    {
        Timeframes.TryParse(code, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_derived_timeframes_are_the_eight_after_the_minute_in_order()
    {
        Timeframes.Aggregated.Select(t => t.ToCode()).ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        Timeframes.All.Select(t => t.ToCode()).ShouldBe(["1m", "5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        Timeframes.Intraday.Select(t => t.ToCode()).ShouldBe(["5m", "10m", "15m", "30m", "1h"]);
    }
}
