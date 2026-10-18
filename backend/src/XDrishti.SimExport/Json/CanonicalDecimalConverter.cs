using System.Text.Json;
using System.Text.Json.Serialization;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Json;

/// <summary>Writes a decimal with the fewest digits that keep its value (<c>0.05</c>, never <c>0.0500</c>) as a raw JSON number, through <see cref="CanonicalText.Number(decimal)"/>. Write only.</summary>
internal sealed class CanonicalDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The export is written, never read back by the system.");

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(CanonicalText.Number(value), skipInputValidation: true);
}
