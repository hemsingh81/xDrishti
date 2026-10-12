using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class IntradayBarCheckTests
{
    private static readonly long Open = new DateTimeOffset(2026, 10, 9, 9, 15, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private static long At(int hour, int minute) => new DateTimeOffset(2026, 10, 9, hour, minute, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private static Bar Good(long time) => new(time, 100, 101, 99, 100.5, 10);

    [Fact]
    public void Bars_inside_a_session_are_kept_sorted_oldest_first()
    {
        var result = IntradayBarCheck.Check([Good(Open + 120), Good(Open), Good(Open + 60)]);

        result.Bars.Select(b => b.EpochSeconds).ShouldBe([Open, Open + 60, Open + 120]);
        (result.Repaired, result.Rejected, result.RejectionExample).ShouldBe((0, 0, null));
    }

    [Theory]
    [InlineData(9, 14, false)]
    [InlineData(9, 15, true)]
    [InlineData(15, 29, true)]
    [InlineData(15, 30, false)]
    [InlineData(3, 0, false)]
    [InlineData(20, 59, false)]
    public void Only_a_bar_inside_a_session_is_kept_which_catches_a_clock_shifted_by_five_and_a_half_hours(int hour, int minute, bool kept)
    {
        var result = IntradayBarCheck.Check([Good(At(hour, minute))]);

        result.Bars.Count.ShouldBe(kept ? 1 : 0);
        result.Rejected.ShouldBe(kept ? 0 : 1);
        (result.RejectionExample is null).ShouldBe(kept);
    }

    [Fact]
    public void A_special_evening_session_is_accepted()
    {
        IntradayBarCheck.Check([Good(At(18, 15)), Good(At(19, 14))]).Bars.Count.ShouldBe(2);
    }

    [Fact]
    public void A_repaired_bar_is_counted_and_a_broken_bar_is_rejected_with_an_example()
    {
        var result = IntradayBarCheck.Check([new Bar(Open, 105, 101, 99, 100, 1), new Bar(Open + 60, -1, 1, 1, 1, 1), new Bar(Open + 120, 100, 101, 99, 100, -5)]);

        result.Bars.ShouldBe([new Bar(Open, 105, 105, 99, 100, 1)]);
        (result.Repaired, result.Rejected).ShouldBe((1, 2));
        result.RejectionExample.ShouldNotBeNull().ShouldEndWith("NonPositivePrice");
    }

    [Fact]
    public void A_minute_that_comes_twice_is_kept_once_the_later_one_winning()
    {
        var result = IntradayBarCheck.Check([Good(Open), Good(Open) with { Close = 7, Low = 1 }]);

        result.Bars.ShouldHaveSingleItem().Close.ShouldBe(7);
        result.Rejected.ShouldBe(0);
    }

    [Fact]
    public void Nothing_in_nothing_out()
    {
        IntradayBarCheck.Check([]).Bars.ShouldBeEmpty();
    }
}
