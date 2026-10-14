using System.Text.Json;
using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Platform.Heartbeats;

/// <summary>Source-generated JSON of the feed's heartbeat details (camelCase: connected, subscribed, lastTickAt, ticksPerSecond, reconnectsToday, droppedPackets, flushMs).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(FeedHeartbeat))]
internal sealed partial class FeedJson : JsonSerializerContext
{
    public static string Write(FeedHeartbeat details) => JsonSerializer.Serialize(details, Default.FeedHeartbeat);

    /// <summary>The details stored in a heartbeat row; null when they are missing or not what the feed writes.</summary>
    public static FeedHeartbeat? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, Default.FeedHeartbeat);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
