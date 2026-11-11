using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace XDrishti.Application.Replay;

/// <summary>
/// The one text form of what a replay stores and hashes (SR-01 part B): JSON with enums as words and the keys of every object in ordinal order, so the same value always gives the same text and the same hash, whatever the order an object was built in.
/// </summary>
internal static class ReplayJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    public static string Canonical<T>(T value) => Sort(JsonSerializer.SerializeToNode(value, Options))?.ToJsonString() ?? "null";

    /// <summary>The value of canonical (or any) text; null when the text is not a <typeparamref name="T"/>.</summary>
    public static T? Read<T>(string text) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The lower-case hex SHA-256 of the UTF-8 text.</summary>
    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray a => new JsonArray([.. a.Select(Sort)]),
        _ => node?.DeepClone(),
    };
}
