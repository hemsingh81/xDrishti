using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.Application.Live.Push;

/// <summary>Source-generated JSON used only to measure what a price costs on the wire (camelCase, as the hub sends it).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(InstrumentPrice))]
[JsonSerializable(typeof(PricesChanged))]
internal sealed partial class PushJson : JsonSerializerContext;

/// <summary>Cuts a list of changed prices into <see cref="PricesChanged"/> messages of at most <c>maxInstruments</c> instruments and <c>maxBytes</c> bytes of JSON, in order. Pure.</summary>
internal static class PriceMessageSplitter
{
    /// <summary>Room kept for the envelope the hub protocol puts around the payload ({"type":1,"target":"pricesChanged","arguments":[ ... ]}), the asOf field and the separator.</summary>
    internal const int EnvelopeBytes = 512;

    public static IReadOnlyList<PricesChanged> Split(IReadOnlyList<InstrumentPrice> prices, DateTimeOffset asOf, int maxInstruments, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInstruments, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, EnvelopeBytes + 1);
        var messages = new List<PricesChanged>();
        var current = new List<InstrumentPrice>();
        var bytes = EnvelopeBytes;
        foreach (var price in prices)
        {
            var size = JsonSerializer.SerializeToUtf8Bytes(price, PushJson.Default.InstrumentPrice).Length + 1; // + the comma
            if (current.Count > 0 && (current.Count >= maxInstruments || bytes + size > maxBytes))
            {
                messages.Add(new PricesChanged(asOf, current));
                current = [];
                bytes = EnvelopeBytes;
            }

            current.Add(price);
            bytes += size;
        }

        if (current.Count > 0)
        {
            messages.Add(new PricesChanged(asOf, current));
        }

        return messages;
    }
}
