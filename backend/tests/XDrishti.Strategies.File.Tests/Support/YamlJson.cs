using System.Globalization;
using System.Text.Json.Nodes;

namespace XDrishti.Strategies.File.Tests.Support;

/// <summary>Converts a strategy file to a JSON tree for the schema: an unquoted scalar that is a plain decimal number becomes a JSON number, every other scalar a string (the same rule as the reader's, so quoted numbers stay strings).</summary>
internal static class YamlJson
{
    public static JsonNode? Convert(string text)
    {
        var problems = new List<FileProblem>();
        var tree = FileTreeReader.Read(text, new FileLimits(), problems);
        return tree is null ? null : Node(tree);
    }

    private static JsonNode? Node(FileNode node) => node switch
    {
        ScalarNode { Quoted: false } scalar when FileShape.IsNumber(scalar.Text) && double.IsFinite(double.Parse(scalar.Text, NumberStyles.Float, CultureInfo.InvariantCulture)) =>
            JsonValue.Create(double.Parse(scalar.Text, NumberStyles.Float, CultureInfo.InvariantCulture)),
        ScalarNode scalar => JsonValue.Create(scalar.Text),
        SequenceNode sequence => new JsonArray([.. sequence.Items.Select(Node)]),
        MappingNode mapping => Object(mapping),
        _ => null,
    };

    private static JsonObject Object(MappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var entry in mapping.Entries)
        {
            result[entry.Key] = Node(entry.Value);
        }

        return result;
    }
}
