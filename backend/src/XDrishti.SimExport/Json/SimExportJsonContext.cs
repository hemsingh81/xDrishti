using System.Text.Json.Serialization;

namespace XDrishti.SimExport.Json;

/// <summary>
/// The source-generated serialiser of <c>run.json</c> (spec P3-04b1, E4): <c>snake_case</c> names in declaration order, compact, the enums through <see cref="ExportSpellings"/>, numbers in the export's format. Generated at build, so a
/// property added to the record by a later slice is written without anyone remembering to write it; the tests that read the record by reflection fail when a new member needs a decision (a spelling, a dictionary).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Converters =
    [
        typeof(SpelledEnumConverterFactory),
        typeof(CanonicalDoubleConverter),
        typeof(CanonicalDecimalConverter),
        typeof(BarConverter),
        typeof(SortedParametersConverter),
    ])]
[JsonSerializable(typeof(RunEnvelope))]
internal sealed partial class SimExportJsonContext : JsonSerializerContext;
