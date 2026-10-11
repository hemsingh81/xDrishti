namespace XDrishti.Infrastructure.Market;

/// <summary>A row of <c>market.instrument_broker_ids</c>: the id a broker uses for an instrument (Dhan: security id + exchange segment).</summary>
internal sealed class InstrumentBrokerIdRecord
{
    public Guid InstrumentId { get; set; }

    public string Broker { get; set; } = string.Empty;

    public string BrokerId { get; set; } = string.Empty;

    public string Segment { get; set; } = string.Empty;
}
