using System.Text;
using System.Text.Json;
using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Tests.Live.Push;

/// <summary>The bounds of a pricesChanged message (ADR 0024 §7): at most 500 instruments and 32 KB, larger changes split in order.</summary>
public sealed class PriceMessageSplitterTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 12, 4, 30, 5, TimeSpan.Zero);

    private static List<InstrumentPrice> Prices(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new InstrumentPrice(Guid.CreateVersion7(), 1234.5678m + i, AsOf.AddSeconds(-i), "live"))];

    /// <summary>What the hub protocol puts on the wire for the message: its envelope around the payload, and the record separator.</summary>
    private static int WireBytes(PricesChanged message)
    {
        var json = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
        return Encoding.UTF8.GetByteCount($$"""{"type":1,"target":"pricesChanged","arguments":[{{json}}]}""") + 1;
    }

    [Fact]
    public void Nothing_changed_gives_no_message()
    {
        PriceMessageSplitter.Split([], AsOf, 500, 32 * 1024).ShouldBeEmpty();
    }

    [Fact]
    public void A_small_change_is_one_message_with_the_time_it_was_read()
    {
        var prices = Prices(3);

        var messages = PriceMessageSplitter.Split(prices, AsOf, 500, 32 * 1024);

        var message = messages.ShouldHaveSingleItem();
        message.AsOf.ShouldBe(AsOf);
        message.Instruments.ShouldBe(prices);
    }

    [Fact]
    public void No_message_has_more_than_the_instrument_limit()
    {
        var prices = Prices(1200);

        var messages = PriceMessageSplitter.Split(prices, AsOf, 100, 10_000_000);

        messages.Count.ShouldBe(12);
        messages.ShouldAllBe(m => m.Instruments.Count == 100);
    }

    [Fact]
    public void No_message_is_larger_than_the_byte_limit_on_the_wire_and_none_has_more_than_500_instruments()
    {
        var prices = Prices(2000);

        var messages = PriceMessageSplitter.Split(prices, AsOf, LiveHubContract.MaxInstrumentsPerMessage, LiveHubContract.MaxMessageBytes);

        messages.Count.ShouldBeGreaterThan(4); // 500 instruments do not fit in 32 KB
        messages.ShouldAllBe(m => m.Instruments.Count <= LiveHubContract.MaxInstrumentsPerMessage);
        messages.ShouldAllBe(m => WireBytes(m) <= LiveHubContract.MaxMessageBytes);
    }

    [Fact]
    public void The_messages_together_are_the_change_in_order_with_nothing_lost_or_repeated()
    {
        var prices = Prices(777);

        var messages = PriceMessageSplitter.Split(prices, AsOf, 500, 32 * 1024);

        messages.SelectMany(m => m.Instruments).ShouldBe(prices);
    }

    [Fact]
    public void A_price_larger_than_the_limit_still_travels_alone_rather_than_being_lost()
    {
        var prices = Prices(2);

        var messages = PriceMessageSplitter.Split(prices, AsOf, 500, PriceMessageSplitter.EnvelopeBytes + 1);

        messages.Count.ShouldBe(2);
        messages.SelectMany(m => m.Instruments).ShouldBe(prices);
    }
}
