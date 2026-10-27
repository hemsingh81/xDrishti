using System.Text.Json;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>Reads <c>plans.jsonl</c> and <c>ideas.jsonl</c> (spec P9-09 "Other file formats"). Pure: text in, cases or a typed failure naming the line.</summary>
public static class EvalSetReader
{
    public static Result<IReadOnlyList<PlanCase>> ReadPlans(string jsonl) => ReadLines(jsonl, ReadPlan);

    public static Result<IReadOnlyList<IdeaCase>> ReadIdeas(string jsonl) => ReadLines(jsonl, ReadIdea);

    private static Result<IReadOnlyList<T>> ReadLines<T>(string jsonl, Func<string, T?> read)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        var cases = new List<T>();
        var number = 0;
        foreach (var line in jsonl.Split('\n'))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            T? item;
            try
            {
                item = read(line);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                item = null;
            }

            if (item is null)
            {
                return Result.Failure<IReadOnlyList<T>>(new Error(EvalCaseReader.InvalidCode, $"line {number} is not a valid case", ErrorKind.Validation));
            }

            cases.Add(item);
        }

        return Result.Success<IReadOnlyList<T>>(cases);
    }

    private static PlanCase? ReadPlan(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var facts = JsonSerializer.Deserialize<BriefingFacts>(root.GetProperty("facts").GetRawText());
        var expect = root.GetProperty("expect");
        var id = root.GetProperty("id").GetString();
        return facts is null || string.IsNullOrWhiteSpace(id)
            ? null
            : new PlanCase(id, root.GetProperty("description").GetString() ?? string.Empty, facts, Strings(expect, "tickets"), Strings(expect, "flags"));
    }

    private static IdeaCase? ReadIdea(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var kind = root.GetProperty("kind").GetString();
        var id = root.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id) || kind is not (IdeaCase.Draft or IdeaCase.Decline or IdeaCase.DeclineOrPartial))
        {
            return null;
        }

        var assertions = new List<IdeaAssertion>();
        foreach (var item in root.GetProperty("assert").EnumerateArray())
        {
            IdeaAssertion? assertion = item switch
            {
                _ when item.TryGetProperty("path", out var path) && item.TryGetProperty("equals", out var equals) => new PathEquals(path.GetString()!, equals.ValueKind == JsonValueKind.String ? equals.GetString()! : equals.GetRawText()),
                _ when item.TryGetProperty("path_exists", out var exists) => new PathExists(exists.GetString()!),
                _ when item.TryGetProperty("yaml_contains_any", out _) => new YamlContainsAny(Strings(item, "yaml_contains_any")),
                _ when item.TryGetProperty("number_covered", out var number) => new NumberCovered(number.GetString()!),
                _ when item.TryGetProperty("decline", out var decline) && decline.GetBoolean() => new DeclineAsserted(),
                _ when item.TryGetProperty("unsupported_mentions", out _) => new UnsupportedMentions(Strings(item, "unsupported_mentions")),
                _ when item.TryGetProperty("sides_include", out _) => new SidesInclude(Strings(item, "sides_include")),
                _ => null,
            };
            if (assertion is null)
            {
                return null;
            }

            assertions.Add(assertion);
        }

        return assertions.Count == 0 ? null : new IdeaCase(id, root.GetProperty("idea").GetString() ?? string.Empty, kind, assertions);
    }

    private static List<string> Strings(JsonElement element, string name) => [.. element.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];
}
