using System.Text.Json;
using System.Text.Json.Nodes;

namespace XDrishti.DhanTrial;

/// <summary>Turns a raw API response into a small, secret-free sample that may be committed as a test fixture.</summary>
internal static class Sanitizer
{
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessToken", "access-token", "tokenId", "consentAppId", "dhanClientId", "dhanClientName", "dhanClientUcc", "pin", "totp", "app_id", "app_secret", "client_id",
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Sanitize(string json, Redactor redactor, int maxArray = 5)
    {
        var node = JsonNode.Parse(json);
        return JsonSerializer.Serialize(Walk(node, redactor, maxArray), Indented);
    }

    private static JsonNode? Walk(JsonNode? node, Redactor redactor, int maxArray)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    copy[key] = SecretKeys.Contains(key) && value is not null ? Redactor.Mask : Walk(value, redactor, maxArray);
                }

                return copy;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array.Take(maxArray))
                {
                    items.Add(Walk(item, redactor, maxArray));
                }

                return items;
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(redactor.Redact(text));
            default:
                return node?.DeepClone();
        }
    }
}
