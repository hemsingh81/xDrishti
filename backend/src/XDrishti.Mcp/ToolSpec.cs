using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace XDrishti.Mcp;

/// <summary>
/// The contract of one tool: name, description and arguments. It produces the JSON Schema (<c>additionalProperties: false</c>) and checks a call's raw arguments against it, answering with the name of the offending property, never an exception (spec P9-02 AC 6).
/// </summary>
internal sealed class ToolSpec(string name, string description, params ArgDef[] arguments)
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public IReadOnlyList<ArgDef> Arguments { get; } = arguments;

    public JsonElement Schema { get; } = BuildSchema(arguments);

    /// <summary>The checked values, or the message that names the first problem.</summary>
    public (ToolArguments? Arguments, string? Problem) Validate(IEnumerable<KeyValuePair<string, object?>>? raw)
    {
        var given = raw?.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) ?? [];
        var known = Arguments.ToDictionary(a => a.Name, StringComparer.Ordinal);
        foreach (var key in given.Keys)
        {
            if (!known.ContainsKey(key))
            {
                return (null, $"Unknown property '{Truncate(key)}'.");
            }
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var arg in Arguments)
        {
            if (!given.TryGetValue(arg.Name, out var value) || value is null || value is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
            {
                if (arg.Required)
                {
                    return (null, $"Property '{arg.Name}' is required.");
                }

                continue;
            }

            var element = value is JsonElement e ? e : JsonSerializer.SerializeToElement(value);
            var problem = Check(arg, element, out var parsed);
            if (problem is not null)
            {
                return (null, problem);
            }

            values[arg.Name] = parsed!;
        }

        return (new ToolArguments(values), null);
    }

    private static string? Check(ArgDef arg, JsonElement element, out object? parsed)
    {
        parsed = null;
        switch (arg.Type)
        {
            case ArgType.String:
                if (element.ValueKind != JsonValueKind.String)
                {
                    return $"Property '{arg.Name}' must be a string.";
                }

                var text = element.GetString()!;
                if (text.Length < arg.Min || text.Length > arg.Max)
                {
                    return $"Property '{arg.Name}' must be {arg.Min} to {arg.Max} characters long.";
                }

                if (arg.Pattern is not null && !Regex.IsMatch(text, arg.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    return $"Property '{arg.Name}' does not match the required pattern {arg.Pattern}.";
                }

                parsed = text;
                return null;
            case ArgType.Integer:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var number))
                {
                    return $"Property '{arg.Name}' must be an integer.";
                }

                if (number < arg.Min || number > arg.Max)
                {
                    return $"Property '{arg.Name}' must be between {arg.Min} and {arg.Max}.";
                }

                parsed = number;
                return null;
            case ArgType.Boolean:
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return $"Property '{arg.Name}' must be true or false.";
                }

                parsed = element.GetBoolean();
                return null;
            case ArgType.StringMap:
                return CheckMap(arg, element, out parsed);
            default:
                return $"Property '{arg.Name}' has an unsupported type.";
        }
    }

    private static string? CheckMap(ArgDef arg, JsonElement element, out object? parsed)
    {
        parsed = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return $"Property '{arg.Name}' must be an object of text values.";
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (map.Count >= arg.MaxItems)
            {
                return $"Property '{arg.Name}' has more than {arg.MaxItems} keys.";
            }

            if (arg.Keys is not null && !arg.Keys.Contains(member.Name, StringComparer.Ordinal))
            {
                return $"Property '{arg.Name}' has an unknown key '{Truncate(member.Name)}'; use {string.Join(", ", arg.Keys)}.";
            }

            if (member.Value.ValueKind != JsonValueKind.String || member.Value.GetString()!.Length > arg.Max)
            {
                return $"Property '{arg.Name}' key '{Truncate(member.Name)}' must be text of at most {arg.Max} characters.";
            }

            map[member.Name] = member.Value.GetString()!;
        }

        parsed = map;
        return null;
    }

    private static string Truncate(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static JsonElement BuildSchema(ArgDef[] arguments)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var arg in arguments)
        {
            var property = new JsonObject { ["description"] = arg.Description };
            if (arg.Type == ArgType.Boolean)
            {
                property["type"] = "boolean";
            }
            else if (arg.Type == ArgType.StringMap)
            {
                property["type"] = "object";
                property["maxProperties"] = arg.MaxItems;
                property["additionalProperties"] = new JsonObject { ["type"] = "string", ["maxLength"] = arg.Max };
            }
            else if (arg.Type == ArgType.String)
            {
                property["type"] = "string";
                if (arg.Min > 1)
                {
                    property["minLength"] = arg.Min; // a minimum of 0 or 1 is left out of the schema the model reads (tokens); Validate still enforces it
                }

                property["maxLength"] = arg.Max;
                if (arg.Pattern is not null)
                {
                    property["pattern"] = arg.Pattern;
                }
            }
            else
            {
                property["type"] = "integer";
                property["minimum"] = arg.Min;
                property["maximum"] = arg.Max;
            }

            properties[arg.Name] = property;
            if (arg.Required)
            {
                required.Add(arg.Name);
            }
        }

        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schema);
    }
}
