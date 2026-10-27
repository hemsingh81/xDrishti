using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>
/// Reads the model's answer as data (spec P9-07 AC 4, Non-functional): strict JSON, no field outside the schema, every list and string bounded, control characters removed. Nothing in the answer is executed; the YAML is only a string until the strategy reader sees it.
/// </summary>
public static class DraftAnswerReader
{
    public const int MaxYamlBytes = 64 * 1024;
    public const int MaxText = 400;
    public const int MaxUnderstood = 8;
    public const int MaxAssumptions = 12;
    public const int MaxListed = 12;

    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "decline", "unsupported", "understood", "assumptions", "not_expressed", "requested_test", "yaml" };

    /// <summary>The answer, or an <c>assistant.answer_shape</c> failure naming the first thing wrong.</summary>
    public static Result<DraftAnswer> Read(string? text)
    {
        var body = StripFence(text ?? string.Empty);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Bad("the answer is not an object");
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!Fields.Contains(property.Name))
                {
                    return Bad($"unknown field {Clip(property.Name)}");
                }
            }

            var decline = root.TryGetProperty("decline", out var d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False && d.GetBoolean();
            var yaml = root.TryGetProperty("yaml", out var y) && y.ValueKind == JsonValueKind.String ? y.GetString() : null;
            if (y.ValueKind is not (JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Undefined))
            {
                return Bad("yaml must be a string or null");
            }

            if (yaml is not null && System.Text.Encoding.UTF8.GetByteCount(yaml) > MaxYamlBytes)
            {
                return Bad("yaml is too large");
            }

            if (!Strings(root, "unsupported", MaxListed, out var unsupported) || !Strings(root, "understood", MaxUnderstood, out var understood))
            {
                return Bad("unsupported and understood must be short lists of strings");
            }

            var assumptions = new List<DraftAssumption>();
            var notExpressed = new List<DraftNotExpressed>();
            if (!Objects(root, "assumptions", MaxAssumptions, ["field", "value", "why"], o => assumptions.Add(new DraftAssumption(Clean(o[0]), Clean(o[1]), Clean(o[2])))) ||
                !Objects(root, "not_expressed", MaxListed, ["what", "why"], o => notExpressed.Add(new DraftNotExpressed(Clean(o[0]), Clean(o[1])))))
            {
                return Bad("assumptions and not_expressed must be short lists of objects of strings");
            }

            var requested = DraftRequestedTest.None;
            if (root.TryGetProperty("requested_test", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in r.EnumerateObject())
                {
                    if (property.Name is not ("from" or "to" or "baskets"))
                    {
                        return Bad("unknown field in requested_test");
                    }
                }

                _ = Strings(r, "baskets", MaxListed, out var baskets);
                requested = new DraftRequestedTest(Text(r, "from"), Text(r, "to"), baskets);
            }

            return Result.Success(new DraftAnswer(decline, unsupported, understood, assumptions, notExpressed, requested, yaml is null ? null : yaml.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }
        catch (JsonException)
        {
            return Bad("the answer is not valid JSON");
        }
    }

    /// <summary>Control characters removed, white space collapsed, cut to <see cref="MaxText"/>.</summary>
    public static string Clean(string? value)
    {
        var kept = new string([.. (value ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c)]);
        kept = string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return kept.Length <= MaxText ? kept : kept[..MaxText];
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? Clean(e.GetString()) : null;

    private static bool Strings(JsonElement parent, string name, int max, out List<string> values)
    {
        values = [];
        if (!parent.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() > max)
        {
            return false;
        }

        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            values.Add(Clean(item.GetString()));
        }

        return true;
    }

    private static bool Objects(JsonElement parent, string name, int max, string[] keys, Action<string[]> add)
    {
        if (!parent.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() > max)
        {
            return false;
        }

        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var values = new string[keys.Length];
            foreach (var property in item.EnumerateObject())
            {
                var index = Array.IndexOf(keys, property.Name);
                if (index < 0 || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                {
                    return false;
                }

                values[index] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
            }

            if (values.Any(v => v is null))
            {
                return false;
            }

            add(values);
        }

        return true;
    }

    private static string StripFence(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var start = t.IndexOf('\n', StringComparison.Ordinal);
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                t = t[(start + 1)..end].Trim();
            }
        }

        return t;
    }

    private static string Clip(string value) => Clean(value).Length <= 30 ? Clean(value) : Clean(value)[..30];

    private static Result<DraftAnswer> Bad(string why) => Result.Failure<DraftAnswer>(Error.Validation(DraftCodes.AnswerShape, "The model's answer does not fit the schema: " + why + "."));
}
