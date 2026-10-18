using System.Text.Json;
using System.Text.Json.Serialization;

namespace XDrishti.SimExport.Json;

/// <summary>Hands every enum of the record to <see cref="SpelledEnumConverter{TEnum}"/>, so that no enum is ever written as a number or as the name of its C# member by default.</summary>
internal sealed class SpelledEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(typeof(SpelledEnumConverter<>).MakeGenericType(typeToConvert));
}
