using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.SimExport.Json;

/// <summary>Writes a double with the number format of the export (the same text as the CSV: round-trip form, invariant culture, <c>-0</c> as <c>0</c>) as a raw JSON number; a NaN or an infinity throws <see cref="ArgumentException"/>. Write only.</summary>
internal sealed class CanonicalDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The export is written, never read back by the system.");

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => WriteNumber(writer, value);

    /// <summary>Writes the number as a raw value, without making a string.</summary>
    public static void WriteNumber(Utf8JsonWriter writer, double value)
    {
        Span<char> text = stackalloc char[NumberText.MaxDoubleChars];
        var length = NumberText.Write(value, text);
        writer.WriteRawValue(text[..length], skipInputValidation: true);
    }
}
