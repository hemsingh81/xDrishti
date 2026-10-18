using System.Text.Json;
using System.Text.Json.Serialization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.SimExport.Json;

/// <summary>Writes a bar with its six values and no derived clock time (<c>Bar.Start</c> would add a second spelling of the epoch). Write only.</summary>
internal sealed class BarConverter : JsonConverter<Bar>
{
    public override Bar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The export is written, never read back by the system.");

    public override void Write(Utf8JsonWriter writer, Bar value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("epoch_seconds", value.EpochSeconds);
        writer.WritePropertyName("open");
        CanonicalDoubleConverter.WriteNumber(writer, value.Open);
        writer.WritePropertyName("high");
        CanonicalDoubleConverter.WriteNumber(writer, value.High);
        writer.WritePropertyName("low");
        CanonicalDoubleConverter.WriteNumber(writer, value.Low);
        writer.WritePropertyName("close");
        CanonicalDoubleConverter.WriteNumber(writer, value.Close);
        writer.WriteNumber("volume", value.Volume);
        writer.WriteEndObject();
    }
}
