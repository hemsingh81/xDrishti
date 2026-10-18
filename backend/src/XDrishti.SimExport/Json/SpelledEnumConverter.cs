using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.SimExport.Json;

/// <summary>Writes an enum as its spelling in <see cref="ExportSpellings"/>; an enum with no spelling, or a member the spelling does not know, throws <see cref="ArgumentOutOfRangeException"/> when the value is written. Write only.</summary>
internal sealed class SpelledEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private Func<TEnum, string>? _speller;

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The export is written, never read back by the system.");

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        _speller ??= ExportSpellings.SpellerFor<TEnum>();
        writer.WriteStringValue(_speller(value));
    }
}
