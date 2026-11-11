using System.Diagnostics;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>BK-08 criteria 7 and 8 (the pure part): the context row of a basket for a day from the BK-06 kernels, and no look-ahead.</summary>
public sealed class BasketContextCalculatorTests
{
    private static readonly Guid Basket = new("00000000-0000-0000-0000-0000000000b1");

    private static readonly DateOnly Day = new(2026, 3, 31);

    private static BasketProfileOptions Options => new();

    // A series of `bars` consecutive days ending on `last`. Rising ends above its 20-day average, falling below it.
    private static MemberSeries Series(string symbol, bool rising, int bars, DateOnly? last = null, double noise = 0)
    {
        var end = last ?? Day;
        var closes = Enumerable.Range(0, bars).Select(i =>
        {
            var step = rising ? i : bars - i;
            return new DailyClose(end.AddDays(i - bars + 1), 100 + step + (noise * Math.Sin(i * (symbol.Length + 1))));
        }).ToList();
        return new MemberSeries(symbol, closes, [], false, false);
    }

    // The spec's example: 10 members, 8 with 20 or more bars, 5 of them above their 20-day average.
    private static List<MemberSeries> Example(int voters = 8, int above = 5)
    {
        var members = new List<MemberSeries>();
        for (var i = 0; i < voters; i++)
        {
            members.Add(Series("V" + i, rising: i < above, bars: 50, noise: 0.3));
        }

        for (var i = voters; i < 10; i++)
        {
            members.Add(Series("S" + i, rising: true, bars: 10));
        }

        return members;
    }

    [Fact]
    public void Ten_members_with_eight_voters_and_five_above_give_b20_0_625_and_a_value_state()
    {
        var row = BasketContextCalculator.Compute(Basket, Day, Example(), BasketContextRow.PointInTime, Options);

        row.NMembers.ShouldBe(10);
        row.NVoters.ShouldBe(8);
        row.B20.ShouldBe(0.625);
        row.State.ShouldBe("value");
        row.MembershipBasis.ShouldBe("point_in_time");
        row.B50.ShouldNotBeNull();
        row.B200.ShouldBeNull();
        row.AdShare.ShouldNotBeNull();
        row.Dispersion.ShouldNotBeNull();
        row.RhoBar.ShouldNotBeNull();
    }

    [Fact]
    public void Four_voters_are_insufficient_and_the_number_is_null()
    {
        var row = BasketContextCalculator.Compute(Basket, Day, Example(voters: 4, above: 2), BasketContextRow.SingleSnapshot, Options);

        row.NVoters.ShouldBe(4);
        row.State.ShouldBe("insufficient");
        row.B20.ShouldBeNull();
        row.MembershipBasis.ShouldBe("single_snapshot");
    }

    [Fact]
    public void A_member_without_a_bar_on_the_day_does_not_vote()
    {
        var members = Example();
        members[0] = Series("V0", rising: true, bars: 30, last: Day.AddDays(-1));

        var row = BasketContextCalculator.Compute(Basket, Day, members, BasketContextRow.PointInTime, Options);

        row.NMembers.ShouldBe(10);
        row.NVoters.ShouldBe(7);
    }

    [Fact]
    public void Property_appending_bars_after_the_day_changes_no_figure_of_that_day()
    {
        var random = new Random(21);
        for (var round = 0; round < 25; round++)
        {
            var members = Enumerable.Range(0, 9).Select(i => Series("M" + i, rising: random.Next(2) == 0, bars: 30 + random.Next(30), noise: random.NextDouble())).ToList();
            var before = BasketContextCalculator.Compute(Basket, Day, members, BasketContextRow.PointInTime, Options);

            var later = members.Select(m => m with { Closes = [.. m.Closes, .. Enumerable.Range(1, 1 + random.Next(10)).Select(k => new DailyClose(Day.AddDays(k), 1 + (random.NextDouble() * 500)))] }).ToList();
            var after = BasketContextCalculator.Compute(Basket, Day, later, BasketContextRow.PointInTime, Options);

            after.ShouldBe(before);
        }
    }

    [Fact(Explicit = true)]
    public void A_basket_of_one_hundred_members_with_a_year_of_bars_is_computed_in_under_three_seconds()
    {
        var members = Enumerable.Range(0, 100).Select(i => Series("M" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture), rising: i % 2 == 0, bars: 300, noise: 1)).ToList();
        BasketContextCalculator.Compute(Basket, Day, members, BasketContextRow.PointInTime, Options);
        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            BasketContextCalculator.Compute(Basket, Day, members, BasketContextRow.PointInTime, Options);
            best = TimeSpan.FromTicks(Math.Min(best.Ticks, watch.Elapsed.Ticks));
        }

        best.TotalSeconds.ShouldBeLessThan(3);
    }
}
