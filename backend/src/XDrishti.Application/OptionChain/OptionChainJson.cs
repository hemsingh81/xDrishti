using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Application.OptionChain;

/// <summary>Source-generated JSON of the option-chain heartbeat details (camelCase).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ChainHeartbeat))]
internal sealed partial class OptionChainJson : JsonSerializerContext
{
}
