using System.Globalization;
using System.Text.Json;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Application.Readiness;

/// <summary>The reasons as the jsonb array the record holds: code, outcome, detail, text, ageSeconds, since. No token, address or client id is ever part of a reason.</summary>
public static class ReadinessJson
{
    public static string Write(IReadOnlyList<ReadinessReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        return JsonSerializer.Serialize(reasons.Select(r => new Dictionary<string, object?>
        {
            ["code"] = r.Code,
            ["outcome"] = ReadinessWords.Word(r.Outcome),
            ["detail"] = r.Detail,
            ["text"] = r.Text,
            ["ageSeconds"] = r.AgeSeconds,
            ["since"] = r.Since?.ToString("O", CultureInfo.InvariantCulture),
        }));
    }

    public static IReadOnlyList<ReadinessReason> Read(string json)
    {
        List<ReadinessReason> list = [];
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string Str(string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
            var age = e.TryGetProperty("ageSeconds", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : (int?)null;
            var since = DateTimeOffset.TryParse(Str("since"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : (DateTimeOffset?)null;
            list.Add(new(Str("code"), ReadinessWords.Outcome(Str("outcome")), Str("detail"), Str("text"), age, since));
        }

        return list;
    }
}
