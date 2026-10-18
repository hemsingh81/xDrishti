using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.SimExport.Json;

/// <summary>Writes the parameters of a strategy (<c>StrategyRecord.Parameters</c>) as an object with the keys in ordinal sorted order, so that the file does not depend on the order a dictionary happens to enumerate. Write only.</summary>
internal sealed class SortedParametersConverter : JsonConverter<IReadOnlyDictionary<string, double>>
{
    public override IReadOnlyDictionary<string, double> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The export is written, never read back by the system.");

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, double> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var name in value.Keys.Order(StringComparer.Ordinal))
        {
            writer.WritePropertyName(name);
            CanonicalDoubleConverter.WriteNumber(writer, value[name]);
        }

        writer.WriteEndObject();
    }
}
