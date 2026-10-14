namespace XDrishti.Infrastructure.Market.Live;

/// <summary>A row of <c>market.live_quotes</c> (ADR 0024 §3): the latest live price of an instrument that is not a holding, and the time of its tick. Unlogged, emptied at the start of each feed window.</summary>
internal sealed class LiveQuoteRecord
{
    public Guid InstrumentId { get; set; }

    public decimal LastPrice { get; set; }

    public DateTimeOffset TickAt { get; set; }
}
