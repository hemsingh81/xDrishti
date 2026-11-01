using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Application.OptionBackfill;

/// <summary>Source-generated JSON of the backfill heartbeat details (camelCase).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(BackfillHeartbeat))]
internal sealed partial class OptionBackfillJson : JsonSerializerContext
{
}
