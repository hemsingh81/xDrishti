using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.OptionChain;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>P14-02 criterion 3 and 6: the slots of the day, the expiries each underlying is asked for, the outcome of each Dhan code.</summary>
public sealed class ChainSlotPlannerTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static DateOnly[] Tuesdays(DateOnly from, int count) => [.. Enumerable.Range(0, count).Select(i => from.AddDays(7 * i))];

    [Fact]
    public void A_day_has_75_slots_from_09_20_to_15_30_every_5_minutes()
    {
        var slots = ChainSlotPlanner.Slots(Monday, 5);

        slots.Count.ShouldBe(75);
        slots[0].ShouldBe(new DateTimeOffset(2026, 10, 12, 9, 20, 0, TimeSpan.FromMinutes(330)));
        slots[^1].ShouldBe(new DateTimeOffset(2026, 10, 12, 15, 30, 0, TimeSpan.FromMinutes(330)));
        slots.Zip(slots.Skip(1), (a, b) => b - a).ShouldAllBe(d => d == TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(10, 38)]
    [InlineData(30, 13)]
    [InlineData(60, 7)]
    public void A_longer_interval_gives_fewer_slots(int minutes, int count)
    {
        ChainSlotPlanner.Slots(Monday, minutes).Count.ShouldBe(count);
    }

    [Fact]
    public void Nifty_gets_the_next_three_weekly_expiries_and_the_next_monthly()
    {
        var list = Tuesdays(new DateOnly(2026, 10, 13), 8); // 13, 20, 27 Oct; 3, 10, 17, 24 Nov; 1 Dec
        var rule = OptionChainSettings.DefaultIndexExpiries[0];

        var expiries = ChainSlotPlanner.IndexExpiries(list, Monday, rule);

        expiries.ShouldBe([new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 24)], "the 3 weekly + the next month-end that is not among them");
    }

    [Fact]
    public void Bank_nifty_gets_the_next_two_monthly_expiries()
    {
        var list = new[] { new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 24), new DateOnly(2026, 12, 29), new DateOnly(2027, 1, 26) };

        ChainSlotPlanner.IndexExpiries(list, Monday, OptionChainSettings.DefaultIndexExpiries[1]).ShouldBe([new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 24)]);
    }

    [Fact]
    public void After_an_expiry_day_the_expired_date_drops_out_and_the_next_one_comes_in()
    {
        var list = Tuesdays(new DateOnly(2026, 10, 13), 6);
        var rule = OptionChainSettings.DefaultIndexExpiries[0];

        var onTheDay = ChainSlotPlanner.IndexExpiries(list, new DateOnly(2026, 10, 13), rule);
        var after = ChainSlotPlanner.IndexExpiries(list, new DateOnly(2026, 10, 14), rule);

        onTheDay.ShouldContain(new DateOnly(2026, 10, 13));
        after.ShouldNotContain(new DateOnly(2026, 10, 13));
        after.ShouldBe([new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17)]);
    }

    [Fact]
    public void A_stock_gets_its_first_expiries_that_are_not_past()
    {
        var list = new[] { new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 24), new DateOnly(2026, 12, 29) };

        ChainSlotPlanner.StockExpiries(list, Monday, 2).ShouldBe([new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 24)]);
        ChainSlotPlanner.StockExpiries([], Monday, 2).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, 200, "ok")]
    [InlineData(null, 429, "rate_limited")]
    [InlineData("DH-904", 400, "rate_limited")]
    [InlineData("DH-901", 401, "token_refused")]
    [InlineData("DH-901", 400, "token_refused")]
    [InlineData(null, 401, "token_refused")]
    [InlineData(null, 403, "token_refused")]
    [InlineData("807", 400, "token_refused")]
    [InlineData("809", 400, "token_refused")]
    [InlineData("DH-902", 403, "not_subscribed")]
    [InlineData("806", 400, "not_subscribed")]
    [InlineData("DH-907", 400, "no_data")]
    [InlineData("DH-908", 500, "server_error")]
    [InlineData("DH-909", 500, "server_error")]
    [InlineData(null, 503, "server_error")]
    [InlineData("DH-905", 400, "server_error")]
    [InlineData(null, 302, "server_error")]
    public void Each_dhan_answer_maps_to_one_outcome_word(string? code, int status, string expected)
    {
        ChainOutcomeMap.From(status, code).ShouldBe(expected);
    }
}
