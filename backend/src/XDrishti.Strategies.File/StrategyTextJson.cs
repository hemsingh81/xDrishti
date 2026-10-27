using System.Text;
using System.Text.Json;

namespace XDrishti.Strategies.File;

/// <summary>
/// The strategy text as plain JSON, for read-only questions about what a text says (spec P9-07 C3 and C6). It goes through the same safe event reader as the strategy file (limits, no anchors, aliases or tags), builds the generic tree and writes it out; every scalar is written as the
/// string it was typed as, nothing is typed or evaluated. Null when the text does not read.
/// </summary>
public static class StrategyTextJson
{
    /// <summary>The JSON of the text, or null when the reader refuses it.</summary>
    public static string? ToJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<FileProblem>();
        var root = FileTreeReader.Read(text, new FileLimits(), problems);
        if (root is null || problems.Count > 0)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, root);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, FileNode node)
    {
        switch (node)
        {
            case ScalarNode s:
                writer.WriteStringValue(s.Text);
                break;
            case SequenceNode q:
                writer.WriteStartArray();
                foreach (var item in q.Items)
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case MappingNode m:
                writer.WriteStartObject();
                foreach (var entry in m.Entries)
                {
                    writer.WritePropertyName(entry.Key);
                    Write(writer, entry.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
