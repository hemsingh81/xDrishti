using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Learning;

/// <summary>
/// What a stored run says about the strategy and the fill rules, split so that a change can be pinned to one knob (spec P6-10, ADR 0076): the effective parameter values, the rest of the definition without its <c>params</c> and without the exits,
/// the exits, and the fill rules. Read from the record text a run was stored with. Pure.
/// </summary>
/// <param name="StrategyId">The strategy id.</param>
/// <param name="Parameters">The effective parameter values by name.</param>
/// <param name="Rest">The canonical definition text without <c>params</c> and without the <c>exits</c> of either side.</param>
/// <param name="Exits">The exits of the two sides as text.</param>
/// <param name="Fills">The canonical fill rules text.</param>
public sealed record RunDefinition(string StrategyId, IReadOnlyDictionary<string, double> Parameters, string Rest, string Exits, string Fills)
{
    /// <summary>Reads the definition of a run from its stored record text and its fill rules; null with a problem when the record cannot be read.</summary>
    public static RunDefinition? TryRead(string recordJson, string fills, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(recordJson);
        ArgumentNullException.ThrowIfNull(fills);
        try
        {
            using var record = JsonDocument.Parse(recordJson);
            if (record.RootElement.ValueKind != JsonValueKind.Object || !record.RootElement.TryGetProperty("strategy", out var strategy)
                || !strategy.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !strategy.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object
                || !strategy.TryGetProperty("definition_json", out var text) || text.ValueKind != JsonValueKind.String)
            {
                problem = "the record of a run has no readable strategy";
                return null;
            }

            var values = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var p in parameters.EnumerateObject())
            {
                if (!p.Value.TryGetDouble(out var v) || !double.IsFinite(v))
                {
                    problem = "a parameter of a run is not a number";
                    return null;
                }

                values[p.Name] = v;
            }

            using var definition = JsonDocument.Parse(text.GetString()!);
            if (definition.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = "the definition of a run is not an object";
                return null;
            }

            problem = null;
            return new RunDefinition(id.GetString()!, values, Copy(definition.RootElement, string.Empty), ExitsOf(definition.RootElement), fills);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            problem = "the record of a run cannot be read";
            return null;
        }
    }

    private static string ExitsOf(JsonElement root)
    {
        var sb = new StringBuilder();
        foreach (var side in new[] { "buy", "sell" })
        {
            sb.Append(side).Append('=');
            if (root.TryGetProperty(side, out var s) && s.ValueKind == JsonValueKind.Object && s.TryGetProperty("exits", out var exits))
            {
                sb.Append(exits.GetRawText());
            }

            sb.Append(';');
        }

        return sb.ToString();
    }

    private static string Copy(JsonElement root, string path)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, root, path);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            element.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        foreach (var p in element.EnumerateObject())
        {
            if ((path.Length == 0 && p.Name == "params") || (path is "buy" or "sell" && p.Name == "exits"))
            {
                continue;
            }

            writer.WritePropertyName(p.Name);
            Write(writer, p.Value, path.Length == 0 ? p.Name : path + "/" + p.Name);
        }

        writer.WriteEndObject();
    }
}
