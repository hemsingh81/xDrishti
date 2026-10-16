using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>
/// The price rules of the live feed (ADR 0024): a tick is applied only when newer than the price held, never writes the previous close, a quote never overwrites a live price newer than its fetch, a sync keeps a quote or live
/// price of the same India day, and an indicative tick (before 09:15 India time) never replaces a quote of the same India day. Monday 2026-10-12, India time.
/// </summary>
public sealed class HoldingLiveTests
{
    /// <summary>10:00 India time.</summary>
    private static readonly DateTimeOffset Ten = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    private static Holding Held() => HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m, price: 60m));

    private static Holding Quoted(DateTimeOffset at, decimal last = 63m, decimal? previous = 62m)
    {
        var holding = Held();
        holding.ApplyQuote(last, previous, at).ShouldBeTrue();
        return holding;
    }

    private static Holding Live(DateTimeOffset at, decimal price = 64m)
    {
        var holding = Quoted(at.AddMinutes(-30));
        holding.ApplyLive(price, at).ShouldBeTrue();
        return holding;
    }

    [Fact]
    public void A_quote_stamps_the_previous_close_with_its_fetch_time_and_a_tick_or_a_sync_never_touches_it()
    {
        var holding = Held();
        holding.PreviousCloseAt.ShouldBeNull();

        holding.ApplyQuote(63m, 62m, Ten.AddMinutes(-30)).ShouldBeTrue();
        holding.ApplyLive(64m, Ten).ShouldBeTrue();
        holding.Reconcile(HoldingData.Row(qty: 100, avgCost: 50m, price: 60m), null, null, HoldingData.Today, Ten.AddMinutes(1));

        (holding.PreviousClose, holding.PreviousCloseAt).ShouldBe((62m, Ten.AddMinutes(-30)));
    }

    [Fact]
    public void A_quote_without_a_previous_close_keeps_the_one_held_and_the_time_it_was_taken()
    {
        var holding = Quoted(Ten.AddMinutes(-30));

        holding.ApplyQuote(65m, null, Ten).ShouldBeTrue();

        (holding.PreviousClose, holding.PreviousCloseAt).ShouldBe((62m, Ten.AddMinutes(-30)));
    }

    [Fact]
    public void A_quote_that_loses_to_a_newer_live_price_still_takes_its_previous_close_and_stamps_it()
    {
        var holding = Held();
        holding.ApplyLive(64m, Ten).ShouldBeTrue();

        holding.ApplyQuote(63m, 62m, Ten.AddSeconds(-5)).ShouldBeTrue(); // the live price is newer than the fetch: only the close is taken

        (holding.LastPriceSource, holding.PreviousClose, holding.PreviousCloseAt).ShouldBe((PriceSource.Live, 62m, Ten.AddSeconds(-5)));
    }

    [Fact]
    public void A_live_tick_sets_price_time_and_source_and_leaves_the_previous_close()
    {
        var holding = Quoted(Ten.AddMinutes(-5));

        var applied = holding.ApplyLive(64.12345m, Ten);

        applied.ShouldBeTrue();
        (holding.LastPrice, holding.LastPriceAt, holding.LastPriceSource, holding.PreviousClose).ShouldBe((64.1235m, Ten, PriceSource.Live, 62m));
    }

    [Fact]
    public void A_live_tick_on_a_holding_that_never_had_a_previous_close_does_not_invent_one()
    {
        var holding = Held();

        holding.ApplyLive(64m, Ten).ShouldBeTrue();

        (holding.PreviousClose, holding.LastPriceSource).ShouldBe((null, PriceSource.Live));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void A_tick_without_a_positive_price_changes_nothing(double price)
    {
        var holding = Live(Ten);

        holding.ApplyLive((decimal)price, Ten.AddSeconds(5)).ShouldBeFalse();

        (holding.LastPrice, holding.LastPriceAt).ShouldBe((64m, Ten));
    }

    [Fact]
    public void A_closed_holding_is_not_priced_by_a_tick()
    {
        var holding = Held();
        holding.CloseMissing(HoldingData.Today, HoldingData.T0.AddHours(1));

        holding.ApplyLive(64m, Ten).ShouldBeFalse();

        holding.LastPriceSource.ShouldBe(PriceSource.Sync);
    }

    [Fact]
    public void A_tick_older_than_the_live_price_held_changes_nothing()
    {
        var holding = Live(Ten);

        holding.ApplyLive(70m, Ten.AddSeconds(-1)).ShouldBeFalse();

        (holding.LastPrice, holding.LastPriceAt).ShouldBe((64m, Ten));
    }

    [Fact]
    public void A_tick_of_the_same_second_with_the_same_price_changes_nothing_and_with_another_price_replaces_it()
    {
        var holding = Live(Ten);

        holding.ApplyLive(64m, Ten).ShouldBeFalse();
        holding.ApplyLive(64.05m, Ten).ShouldBeTrue(); // Dhan stamps whole seconds: a second trade of the same second is a new tick

        (holding.LastPrice, holding.LastPriceAt).ShouldBe((64.05m, Ten));
    }

    [Fact]
    public void A_newer_tick_replaces_the_live_price()
    {
        var holding = Live(Ten);

        holding.ApplyLive(65m, Ten.AddSeconds(2)).ShouldBeTrue();

        (holding.LastPrice, holding.LastPriceAt).ShouldBe((65m, Ten.AddSeconds(2)));
    }

    [Fact]
    public void A_live_price_never_overwrites_a_newer_quote()
    {
        var holding = Quoted(Ten, last: 63m);

        holding.ApplyLive(70m, Ten.AddSeconds(-3)).ShouldBeFalse(); // the tick is older than the quote's fetch

        (holding.LastPrice, holding.LastPriceSource).ShouldBe((63m, PriceSource.Quote));
    }

    [Fact]
    public void A_quote_never_overwrites_a_live_price_newer_than_its_fetch_time_but_still_gives_the_previous_close()
    {
        var holding = Live(Ten, 64m);

        var applied = holding.ApplyQuote(63m, 61m, Ten.AddSeconds(-2));

        applied.ShouldBeTrue();
        (holding.LastPrice, holding.LastPriceAt, holding.LastPriceSource, holding.PreviousClose).ShouldBe((64m, Ten, PriceSource.Live, 61m));
    }

    [Fact]
    public void A_quote_without_a_previous_close_and_an_older_fetch_than_the_live_price_changes_nothing()
    {
        var holding = Live(Ten, 64m);

        holding.ApplyQuote(63m, null, Ten.AddSeconds(-2)).ShouldBeFalse();

        (holding.LastPrice, holding.LastPriceSource, holding.PreviousClose).ShouldBe((64m, PriceSource.Live, 62m));
    }

    [Fact]
    public void A_quote_fetched_after_the_live_price_takes_over()
    {
        var holding = Live(Ten, 64m);

        holding.ApplyQuote(63m, 61m, Ten.AddSeconds(1)).ShouldBeTrue();

        (holding.LastPrice, holding.LastPriceSource, holding.LastPriceAt).ShouldBe((63m, PriceSource.Quote, Ten.AddSeconds(1)));
    }

    [Fact]
    public void A_sync_on_the_same_india_day_keeps_the_live_price_and_never_touches_the_previous_close()
    {
        var holding = Live(Ten, 64m);
        var syncAt = Ten.AddHours(5);

        holding.Reconcile(HoldingData.Row(qty: 100, avgCost: 51m, price: 61m), null, null, new DateOnly(2026, 10, 12), syncAt);

        (holding.LastPrice, holding.LastPriceAt, holding.LastPriceSource, holding.PreviousClose).ShouldBe((64m, Ten, PriceSource.Live, 62m));
        holding.AvgCost.ShouldBe(51m);
    }

    [Fact]
    public void A_sync_on_a_later_india_day_takes_the_synced_price_over_a_live_one()
    {
        var holding = Live(Ten, 64m);
        var nextDay = new DateTimeOffset(2026, 10, 12, 19, 0, 0, TimeSpan.Zero); // 13 Oct 00:30 India time

        holding.Reconcile(HoldingData.Row(price: 61m), null, null, new DateOnly(2026, 10, 13), nextDay);

        (holding.LastPrice, holding.LastPriceSource, holding.PreviousClose).ShouldBe((61m, PriceSource.Sync, 62m));
    }

    [Fact]
    public void An_indicative_tick_before_the_open_does_not_replace_a_quote_of_the_same_india_day()
    {
        var quotedAt = new DateTimeOffset(2026, 10, 12, 3, 33, 0, TimeSpan.Zero); // 09:03 India time (a manual run before the open)
        var holding = Quoted(quotedAt);

        holding.ApplyLive(70m, quotedAt.AddMinutes(5)).ShouldBeFalse(); // 09:08: newer, but indicative

        (holding.LastPrice, holding.LastPriceAt, holding.LastPriceSource).ShouldBe((63m, quotedAt, PriceSource.Quote));
    }

    [Fact]
    public void An_indicative_tick_replaces_a_quote_of_an_earlier_india_day()
    {
        var yesterday = Ten.AddDays(-1);
        var holding = Quoted(yesterday);

        holding.ApplyLive(70m, new DateTimeOffset(2026, 10, 12, 3, 40, 0, TimeSpan.Zero)).ShouldBeTrue(); // 09:10

        (holding.LastPrice, holding.LastPriceSource).ShouldBe((70m, PriceSource.Live));
    }

    [Fact]
    public void An_indicative_tick_replaces_a_synced_price_and_a_live_one()
    {
        var holding = Held();

        holding.ApplyLive(70m, new DateTimeOffset(2026, 10, 12, 3, 40, 0, TimeSpan.Zero)).ShouldBeTrue(); // 09:10
        holding.ApplyLive(71m, new DateTimeOffset(2026, 10, 12, 3, 41, 0, TimeSpan.Zero)).ShouldBeTrue();

        (holding.LastPrice, holding.LastPriceSource).ShouldBe((71m, PriceSource.Live));
    }

    [Fact]
    public void A_tick_from_the_open_on_replaces_a_quote_of_the_same_day_when_newer()
    {
        var holding = Quoted(Ten);

        holding.ApplyLive(70m, Ten.AddSeconds(1)).ShouldBeTrue();

        holding.LastPriceSource.ShouldBe(PriceSource.Live);
    }
}
