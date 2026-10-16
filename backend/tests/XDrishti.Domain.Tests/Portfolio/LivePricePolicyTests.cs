using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The rule table behind <see cref="Holding.ApplyLive"/> and the SQL of the stores: which price wins (ADR 0024). Monday 2026-10-12, India time = UTC + 5:30.</summary>
public sealed class LivePricePolicyTests
{
    private static DateTimeOffset India(int day, int hour, int minute, int second = 0) => new DateTimeOffset(2026, 10, day, hour, minute, second, TimeSpan.Zero).AddMinutes(-330);

    [Theory]
    [InlineData(9, 0, true)]
    [InlineData(9, 14, true)]
    [InlineData(9, 15, false)]
    [InlineData(15, 29, false)]
    public void A_tick_before_09_15_india_time_is_indicative(int hour, int minute, bool indicative)
    {
        LivePricePolicy.IsIndicative(India(12, hour, minute)).ShouldBe(indicative);
    }

    [Fact]
    public void The_second_before_the_open_is_indicative_and_the_open_is_not()
    {
        LivePricePolicy.IsIndicative(India(12, 9, 14, 59)).ShouldBeTrue();
        LivePricePolicy.IsIndicative(India(12, 9, 15)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(PriceSource.Sync, true)]
    [InlineData(PriceSource.Quote, true)]
    [InlineData(PriceSource.Live, true)]
    public void Nothing_held_with_a_time_is_always_replaced(PriceSource source, bool replaced)
    {
        LivePricePolicy.LiveReplaces(source, null, India(12, 10, 0), 100m, 101m).ShouldBe(replaced);
    }

    [Theory]
    [InlineData(PriceSource.Sync, 1, true)]
    [InlineData(PriceSource.Quote, 1, true)]
    [InlineData(PriceSource.Live, 1, true)]
    [InlineData(PriceSource.Sync, 0, false)]
    [InlineData(PriceSource.Quote, 0, false)]
    [InlineData(PriceSource.Live, 0, false)]
    [InlineData(PriceSource.Quote, -1, false)]
    public void A_tick_replaces_only_a_strictly_older_price(PriceSource source, int secondsAfterHeld, bool replaced)
    {
        var held = India(12, 10, 0);

        LivePricePolicy.LiveReplaces(source, held, held.AddSeconds(secondsAfterHeld), 100m, 100m).ShouldBe(replaced);
    }

    [Theory]
    [InlineData(PriceSource.Sync)]
    [InlineData(PriceSource.Quote)]
    [InlineData(PriceSource.Live)]
    public void A_tick_of_the_same_second_replaces_the_price_when_it_differs_and_is_a_duplicate_when_it_does_not(PriceSource source)
    {
        var held = India(12, 10, 0);

        LivePricePolicy.LiveReplaces(source, held, held, 100m, 100.05m).ShouldBeTrue();
        LivePricePolicy.LiveReplaces(source, held, held, 100m, 100m).ShouldBeFalse();
        LivePricePolicy.LiveReplaces(source, held, held.AddSeconds(-1), 100m, 100.05m).ShouldBeFalse();
    }

    [Fact]
    public void An_indicative_tick_is_stopped_by_a_quote_of_the_same_india_day_only()
    {
        var tick = India(12, 9, 10);

        LivePricePolicy.LiveReplaces(PriceSource.Quote, India(12, 9, 5), tick, 100m, 101m).ShouldBeFalse();
        LivePricePolicy.LiveReplaces(PriceSource.Quote, India(11, 15, 30), tick, 100m, 101m).ShouldBeTrue();
        LivePricePolicy.LiveReplaces(PriceSource.Sync, India(12, 9, 5), tick, 100m, 101m).ShouldBeTrue();
        LivePricePolicy.LiveReplaces(PriceSource.Live, India(12, 9, 5), tick, 100m, 101m).ShouldBeTrue();
    }

    [Theory]
    [InlineData(PriceSource.Live, -1, false)] // the live price is newer than the fetch
    [InlineData(PriceSource.Live, 0, true)]
    [InlineData(PriceSource.Live, 1, true)]
    [InlineData(PriceSource.Quote, -5, true)]
    [InlineData(PriceSource.Sync, -5, true)]
    public void A_quote_is_stopped_only_by_a_live_price_newer_than_its_fetch(PriceSource source, int secondsAfterHeld, bool replaced)
    {
        var held = India(12, 10, 0);

        LivePricePolicy.QuoteReplaces(source, held, held.AddSeconds(secondsAfterHeld)).ShouldBe(replaced);
    }

    [Fact]
    public void A_quote_replaces_a_live_price_without_a_time()
    {
        LivePricePolicy.QuoteReplaces(PriceSource.Live, null, India(12, 10, 0)).ShouldBeTrue();
    }
}
