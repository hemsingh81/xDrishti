using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The price rules of ADR 0019 §3: a quote sets price, previous close, time and source; a sync keeps a quote of the same India day and never touches the previous close.</summary>
public sealed class HoldingQuoteTests
{
    /// <summary>Monday 2026-10-12, 10:30 India time (05:00 UTC).</summary>
    private static readonly DateTimeOffset QuoteAt = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);

    private static Holding Held() => HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m, price: 60m));

    private static Holding Quoted(decimal last = 63.5m, decimal? previous = 62m)
    {
        var holding = Held();
        holding.ApplyQuote(last, previous, QuoteAt).ShouldBeTrue();
        return holding;
    }

    [Fact]
    public void A_new_holding_has_the_sync_price_and_no_previous_close()
    {
        var holding = Held();

        (holding.LastPrice, holding.LastPriceSource, holding.PreviousClose).ShouldBe((60m, PriceSource.Sync, null));
    }

    [Fact]
    public void A_quote_sets_the_price_the_previous_close_the_time_and_the_source()
    {
        var holding = Held();

        var applied = holding.ApplyQuote(63.5m, 62m, QuoteAt);

        applied.ShouldBeTrue();
        (holding.LastPrice, holding.PreviousClose, holding.LastPriceAt, holding.LastPriceSource).ShouldBe((63.5m, 62m, QuoteAt, PriceSource.Quote));
    }

    [Fact]
    public void A_quote_is_rounded_to_four_decimals_like_every_price()
    {
        var holding = Held();

        holding.ApplyQuote(63.12345m, 62.00005m, QuoteAt);

        (holding.LastPrice, holding.PreviousClose).ShouldBe((63.1235m, 62.0001m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public void A_quote_without_a_positive_last_price_changes_nothing(double last)
    {
        var holding = Quoted();

        var applied = holding.ApplyQuote((decimal)last, 70m, QuoteAt.AddMinutes(5));

        applied.ShouldBeFalse();
        (holding.LastPrice, holding.PreviousClose, holding.LastPriceAt).ShouldBe((63.5m, 62m, QuoteAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_zero_or_negative_previous_close_is_ignored_and_the_one_held_stays(double previous)
    {
        var holding = Quoted(previous: 62m);

        var applied = holding.ApplyQuote(64m, (decimal)previous, QuoteAt.AddMinutes(5));

        applied.ShouldBeTrue();
        (holding.LastPrice, holding.PreviousClose, holding.LastPriceAt).ShouldBe((64m, 62m, QuoteAt.AddMinutes(5)));
    }

    [Fact]
    public void A_missing_previous_close_is_ignored_and_the_one_held_stays()
    {
        var holding = Quoted(previous: 62m);

        holding.ApplyQuote(64m, null, QuoteAt.AddMinutes(5)).ShouldBeTrue();

        (holding.LastPrice, holding.PreviousClose).ShouldBe((64m, 62m));
    }

    [Fact]
    public void A_closed_holding_is_not_quoted()
    {
        var holding = Held();
        holding.CloseMissing(HoldingData.Today, HoldingData.T0.AddHours(1));

        var applied = holding.ApplyQuote(63m, 62m, QuoteAt);

        applied.ShouldBeFalse();
        (holding.LastPriceSource, holding.PreviousClose).ShouldBe((PriceSource.Sync, null));
    }

    [Fact]
    public void A_sync_on_the_same_india_day_as_a_quote_keeps_the_quote_and_never_touches_the_previous_close()
    {
        var holding = Quoted();
        var syncAt = QuoteAt.AddHours(5); // 15:30 India time, the same day

        holding.Reconcile(HoldingData.Row(qty: 100, avgCost: 51m, price: 61m), null, null, new DateOnly(2026, 10, 12), syncAt);

        (holding.LastPrice, holding.LastPriceAt, holding.LastPriceSource, holding.PreviousClose).ShouldBe((63.5m, QuoteAt, PriceSource.Quote, 62m));
        (holding.AvgCost, holding.SyncedAt).ShouldBe((51m, syncAt)); // everything else of the sync still applies
    }

    [Fact]
    public void The_india_day_decides_not_the_utc_day()
    {
        var late = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero); // 13 Oct 01:30 India time
        var holding = Held();
        holding.ApplyQuote(63.5m, 62m, late);

        holding.Reconcile(HoldingData.Row(price: 61m), null, null, new DateOnly(2026, 10, 13), new DateTimeOffset(2026, 10, 13, 2, 0, 0, TimeSpan.Zero)); // 13 Oct 07:30 India time

        holding.LastPriceSource.ShouldBe(PriceSource.Quote);
    }

    [Fact]
    public void A_sync_after_midnight_india_time_replaces_the_quote_of_the_day_before_and_keeps_the_previous_close()
    {
        var holding = Quoted();
        var nextDay = new DateTimeOffset(2026, 10, 12, 19, 0, 0, TimeSpan.Zero); // 13 Oct 00:30 India time

        holding.Reconcile(HoldingData.Row(price: 61m), null, null, new DateOnly(2026, 10, 13), nextDay);

        (holding.LastPrice, holding.LastPriceAt, holding.LastPriceSource, holding.PreviousClose).ShouldBe((61m, nextDay, PriceSource.Sync, 62m));
    }

    [Fact]
    public void A_sync_on_a_holding_that_never_had_a_quote_sets_the_price_as_before()
    {
        var holding = Held();

        holding.Reconcile(HoldingData.Row(price: 70m), null, null, HoldingData.Today, HoldingData.T0.AddHours(1));

        (holding.LastPrice, holding.LastPriceSource, holding.PreviousClose).ShouldBe((70m, PriceSource.Sync, null));
    }

    [Fact]
    public void A_sync_without_a_price_keeps_the_price_and_the_source()
    {
        var holding = Quoted();

        holding.Reconcile(HoldingData.Row(price: 0m), null, null, new DateOnly(2026, 10, 13), QuoteAt.AddDays(1));

        (holding.LastPrice, holding.LastPriceSource).ShouldBe((63.5m, PriceSource.Quote));
    }

    [Fact]
    public void A_quote_after_a_sync_takes_the_price_over_again()
    {
        var holding = Quoted();
        holding.Reconcile(HoldingData.Row(price: 61m), null, null, new DateOnly(2026, 10, 13), QuoteAt.AddDays(1)); // a later day: the sync price
        holding.LastPriceSource.ShouldBe(PriceSource.Sync);

        holding.ApplyQuote(64m, 63m, QuoteAt.AddDays(1).AddHours(1));

        (holding.LastPrice, holding.PreviousClose, holding.LastPriceSource).ShouldBe((64m, 63m, PriceSource.Quote));
    }

    [Fact]
    public void A_holding_that_comes_back_after_being_closed_has_no_previous_close()
    {
        var holding = Quoted();
        holding.CloseMissing(new DateOnly(2026, 10, 12), QuoteAt.AddHours(1));
        holding.PreviousClose.ShouldBe(62m); // closed holdings keep what they had

        holding.Reconcile(HoldingData.Row(price: 61m), null, null, new DateOnly(2026, 10, 13), QuoteAt.AddDays(1));

        (holding.Status, holding.PreviousClose, holding.LastPrice).ShouldBe((HoldingStatus.Open, null, 61m));
    }
}
