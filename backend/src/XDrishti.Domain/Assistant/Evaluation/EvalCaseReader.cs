using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>Reads the lines of <c>data-questions.jsonl</c> (spec P9-09). Pure: text in, cases or a typed failure out; a malformed line is named, never skipped.</summary>
public static class EvalCaseReader
{
    public const string InvalidCode = "eval.invalid_set";

    /// <summary>Reads every non-blank line; fails on the first line that is not a case, with its number.</summary>
    public static Result<IReadOnlyList<EvalCase>> ReadLines(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        var cases = new List<EvalCase>();
        var number = 0;
        foreach (var line in jsonl.Split('\n'))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var read = ReadLine(line);
            if (read.IsFailure)
            {
                return Result.Failure<IReadOnlyList<EvalCase>>(new Error(InvalidCode, $"line {number}: {read.Error.Message}", ErrorKind.Validation));
            }

            cases.Add(read.Value);
        }

        return cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == cases.Count
            ? Result.Success<IReadOnlyList<EvalCase>>(cases)
            : Result.Failure<IReadOnlyList<EvalCase>>(new Error(InvalidCode, "an id is used twice", ErrorKind.Validation));
    }

    public static Result<EvalCase> ReadLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var parts = new List<ExpectedPart>();
            foreach (var part in root.GetProperty("expected").GetProperty("parts").EnumerateArray())
            {
                var read = ReadPart(part);
                if (read is null)
                {
                    return Fail("an expected part is not valid");
                }

                parts.Add(read);
            }

            var allowed = new List<EvalFraction>();
            foreach (var item in root.GetProperty("allowed_numbers").EnumerateArray())
            {
                var value = EvalFraction.Parse(item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText());
                if (value is null)
                {
                    return Fail("an allowed number is not a number");
                }

                allowed.Add(value.Value);
            }

            var id = root.GetProperty("id").GetString();
            return string.IsNullOrWhiteSpace(id) || parts.Count == 0
                ? Fail("a case needs an id and at least one expected part")
                : Result.Success(new EvalCase(id, root.GetProperty("category").GetString()!, root.GetProperty("question").GetString()!, parts, Strings(root, "anchors"), allowed, Strings(root, "tools_expected")));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Fail("the line is not a case");
        }
    }

    private static ExpectedPart? ReadPart(JsonElement part)
    {
        switch (part.GetProperty("kind").GetString())
        {
            case "number":
                var value = EvalFraction.Parse(part.GetProperty("value").ValueKind == JsonValueKind.String ? part.GetProperty("value").GetString() : part.GetProperty("value").GetRawText());
                var min = part.GetProperty("min_decimals").GetInt32();
                return value is null || min is < 0 or > 12 ? null : new NumberPart(value.Value, min, part.TryGetProperty("unit", out var unit) ? unit.GetString() : null);
            case "integer":
                return new IntegerPart(part.GetProperty("value").GetInt64());
            case "text":
                var any = Strings(part, "contains_any");
                return any.Count == 0 ? null : new TextPart(any, Strings(part, "forbid"));
            case "honest":
                var phrases = Strings(part, "phrases");
                return phrases.Count == 0 ? null : new HonestPart(part.GetProperty("state").GetString()!, phrases, part.TryGetProperty("withheld", out var w) ? w.GetString() : null);
            default:
                return null;
        }
    }

    private static List<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) ? [.. array.EnumerateArray().Select(e => e.GetString()!)] : [];

    private static Result<EvalCase> Fail(string message) => Result.Failure<EvalCase>(new Error(InvalidCode, message, ErrorKind.Validation));
}
