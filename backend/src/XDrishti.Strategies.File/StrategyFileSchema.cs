using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File;

/// <summary>
/// The JSON Schema (draft 2020-12) of the strategy file, generated from <see cref="FileShape"/> and the name tables (P3-02c D6): the editor's help and the shape check of a client. It judges shape only: the conditions are strings,
/// and <c>StrategyCompiler</c> is the only judge of the rules, the ranges and the relations between fields. A number is a JSON number; a parameter name is a string; a time is a string, so the YAML must be converted with plain scalars as
/// numbers and quoted ones as strings.
/// </summary>
public static class StrategyFileSchema
{
    /// <summary>The <c>$id</c> of the schema.</summary>
    public const string Id = "urn:xdrishti:strategy:1";

    private static readonly Lazy<string> Text = new(Build);

    /// <summary>The schema as JSON text (indented, <c>\n</c> line ends, no trailing newline). Generated once per process.</summary>
    public static string Json() => Text.Value;

    private static string Build()
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = Id,
            ["title"] = "xDrishti strategy file, schema 1",
            ["type"] = "object",
            ["required"] = Array("strategy"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["strategy"] = Ref("strategy"), ["test"] = new JsonObject(), ["universe"] = new JsonObject() },
            ["$defs"] = Defs(),
        };
        var options = new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return schema.ToJsonString(options);
    }

    private static JsonObject Defs() => new()
    {
        ["quantity"] = new JsonObject
        {
            ["anyOf"] = new JsonArray(
                new JsonObject { ["type"] = "number" },
                new JsonObject { ["type"] = "string", ["pattern"] = "^(?!(" + string.Join('|', FileShape.Reserved.Order(StringComparer.Ordinal)) + ")$)" + FileShape.ParamNamePattern + "$" }),
        },
        ["time"] = new JsonObject { ["type"] = "string", ["pattern"] = "^" + FileShape.TimePattern + "$" },
        ["conditionList"] = new JsonObject
        {
            ["type"] = "object",
            ["minProperties"] = 1,
            ["maxProperties"] = 1,
            ["additionalProperties"] = false,
            ["properties"] = Properties(StrategyNames.Modes.Names.Select(m => (m, (JsonNode)new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = StrategyDefinition.MaxItemsPerList,
                ["items"] = new JsonObject { ["type"] = "string", ["maxLength"] = new FileLimits().MaxScalarLength },
            }))),
        },
        ["values"] = new JsonObject
        {
            ["anyOf"] = new JsonArray(
                Ref("quantity"),
                new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = ExitParameterInfo.MaxListValues, ["items"] = Ref("quantity") }),
        },
        ["entry"] = Closed(new Dictionary<string, JsonNode> { ["type"] = Enum(StrategyNames.Entries.Names) }, "type"),
        ["stop"] = Closed(
            new Dictionary<string, JsonNode> { ["type"] = Enum(StrategyNames.Stops.Names), ["timeframe"] = Enum(FileShape.TimeframeCodes) }
                .Concat(FileShape.StopFields.Skip(1).ToDictionary(f => f, f => (JsonNode)Ref("quantity"))).ToDictionary(p => p.Key, p => p.Value),
            "type"),
        ["exit"] = new JsonObject { ["oneOf"] = new JsonArray([.. StrategyNames.Policies.Names.Select(name => (JsonNode)Flat("policy", name, StrategyNames.Policies.TryParse(name, out var policy) ? ExitParameterInfo.Of(policy) : []))]) },
        ["overlay"] = new JsonObject { ["oneOf"] = new JsonArray([.. StrategyNames.Overlays.Names.Select(name => (JsonNode)Flat("kind", name, StrategyNames.Overlays.TryParse(name, out var kind) ? ExitParameterInfo.Of(kind) : []))]) },
        ["timeRules"] = Closed(
            new Dictionary<string, JsonNode>
            {
                ["first_entry"] = Ref("time"),
                ["last_entry"] = Ref("time"),
                ["square_off"] = Ref("time"),
                ["max_hold_sessions"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
            }),
        ["side"] = Closed(SideProperties()),
        ["strategy"] = Strategy(),
    };

    private static JsonObject Strategy()
    {
        var properties = new Dictionary<string, JsonNode>
        {
            ["schema"] = new JsonObject { ["const"] = 1 },
            ["id"] = new JsonObject { ["type"] = "string" },
            ["version"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
            ["style"] = Enum(StrategyNames.Styles.Names),
            ["timeframes"] = StringList(Enum(FileShape.TimeframeCodes)),
            ["sides"] = StringList(Enum(StrategyNames.Sides.Names)),
            ["params"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "number" } },
            [FileShape.SellKey] = Ref("side"),
            ["time_rules"] = Ref("timeRules"),
            ["filters"] = StringList(new JsonObject { ["type"] = "string" }),
            ["test"] = new JsonObject(),
            ["universe"] = new JsonObject(),
        };
        foreach (var (key, node) in SideProperties())
        {
            properties[key] = node;
        }

        return Closed(properties, "id", "style");
    }

    private static Dictionary<string, JsonNode> SideProperties() => new()
    {
        ["setup"] = Ref("conditionList"),
        ["trigger"] = Ref("conditionList"),
        ["invalidate"] = Ref("conditionList"),
        ["entry"] = Ref("entry"),
        ["stop"] = Ref("stop"),
        ["exits"] = new JsonObject { ["type"] = "array", ["items"] = Ref("exit") },
        ["overlays"] = new JsonObject { ["type"] = "array", ["items"] = Ref("overlay") },
    };

    private static JsonObject Flat(string kindKey, string name, IReadOnlyList<ExitParameterInfo> known)
    {
        var properties = new Dictionary<string, JsonNode> { [kindKey] = new JsonObject { ["const"] = name } };
        foreach (var parameter in known)
        {
            properties[parameter.Name] = Ref("values");
        }

        return Closed(properties, kindKey);
    }

    private static JsonObject Closed(Dictionary<string, JsonNode> properties, params string[] required)
    {
        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = Properties(properties.Select(p => (p.Key, p.Value))),
        };
        if (required.Length > 0)
        {
            result["required"] = Array(required);
        }

        return result;
    }

    private static JsonObject Properties(IEnumerable<(string Key, JsonNode Node)> items)
    {
        var result = new JsonObject();
        foreach (var (key, node) in items)
        {
            result[key] = node.DeepClone();
        }

        return result;
    }

    private static JsonObject StringList(JsonNode items) => new() { ["type"] = "array", ["items"] = items.DeepClone() };

    private static JsonObject Enum(IEnumerable<string> names) => new() { ["enum"] = Array([.. names]) };

    private static JsonObject Ref(string name) => new() { ["$ref"] = "#/$defs/" + name };

    private static JsonArray Array(params string[] values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);
}
