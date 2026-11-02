using System.Globalization;
using System.Text;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The labelled announcement file of set D (P15-07): one JSON object per line with exactly the keys of <c>announcements.schema.json</c> (<c>id, symbol, published_at, source, text, label</c> and the optional <c>category</c>), no other key, no duplicate id. <see cref="Parse"/>
/// returns the rows or the first problem with its line number (never the text). <see cref="Write"/> is deterministic. Pure.
/// </summary>
public static class AnnouncementSet
{
    public const string InvalidCode = "eval.announcements_invalid";

    private static readonly HashSet<string> Required = ["id", "symbol", "published_at", "source", "text", "label"];

    public static string Write(IEnumerable<LabelledAnnouncement> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("id", row.Id);
                json.WriteString("symbol", row.Symbol);
                json.WriteString("published_at", row.PublishedAt);
                json.WriteString("source", row.Source);
                json.WriteString("text", row.Text);
                json.WriteString("label", row.Material ? "material" : "non_material");
                if (row.Category is not null)
                {
                    json.WriteString("category", row.Category);
                }

                json.WriteEndObject();
            }

            text.Append(Encoding.UTF8.GetString(stream.ToArray())).Append('\n');
        }

        return text.ToString();
    }

    public static Result<IReadOnlyList<LabelledAnnouncement>> Parse(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        var rows = new List<LabelledAnnouncement>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in jsonl.ReplaceLineEndings("\n").Split('\n'))
        {
            number++;
            if (raw.Length == 0)
            {
                continue;
            }

            var row = ParseLine(raw);
            if (row is null || !ids.Add(row.Id))
            {
                return Result.Failure<IReadOnlyList<LabelledAnnouncement>>(Error.Validation(InvalidCode, $"data/eval/sets/announcements.jsonl line {number} is not a valid row of announcements.schema.json" + (row is null ? string.Empty : " (duplicate id)") + "."));
            }

            rows.Add(row);
        }

        return Result.Success<IReadOnlyList<LabelledAnnouncement>>(rows);
    }

    private static LabelledAnnouncement? ParseLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if ((!Required.Contains(property.Name) && property.Name != "category") || property.Value.ValueKind != JsonValueKind.String || !values.TryAdd(property.Name, property.Value.GetString()!))
                {
                    return null;
                }
            }

            if (!Required.All(values.ContainsKey) || Required.Any(k => values[k].Length == 0) || values["label"] is not ("material" or "non_material")
                || !DateTimeOffset.TryParse(values["published_at"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return null;
            }

            return new LabelledAnnouncement(values["id"], values["symbol"], values["published_at"], values["source"], values["text"], values["label"] == "material", values.GetValueOrDefault("category"));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
