using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary><c>describe_table</c> (spec P9-02 AC 9): the columns of one view with type, kind, class, unit and meaning.</summary>
internal sealed class DescribeTableTool(ToolRunner runner, IAiCatalog catalog) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "describe_table",
        "Describes one view returned by list_tables: grain, limits and every column (type, kind, class, unit, meaning). Read-only.",
        new ArgDef("name", ArgType.String, true, "View name as listed, for example instruments.", 1, 63, "^[a-z0-9_]{1,63}$"));

    private static readonly ToolColumn[] Columns = [new("name", false), new("type", false), new("kind", false), new("class", false), new("unit", false), new("description", false)];

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var table = await catalog.DescribeAsync(arguments.String("name")!, cancellationToken);
        if (table is null)
        {
            return ToolOutcome.Fail(ToolErrorCodes.TableUnknown, "There is no such view; call list_tables for the names.");
        }

        return new ToolOutcome
        {
            Columns = Columns,
            Rows = [.. table.Columns.Select(c => (IReadOnlyList<object?>)[c.Name, c.DbType, c.Kind.ToString().ToLowerInvariant(), c.Class.ToString(), c.Unit, c.Description])],
            Data = new JsonObject
            {
                ["name"] = table.Name,
                ["description"] = table.Description,
                ["grain"] = table.Grain,
                ["classes"] = new JsonArray([.. table.Classes.Select(c => (JsonNode?)JsonValue.Create(c))]),
                ["limits"] = new JsonArray([.. table.Limits.Select(l => (JsonNode?)JsonValue.Create(l))]),
            },
            Evidence = new ToolEvidence("the reviewed manifest of the view joined to the live database types", ["aiv." + table.Name], null, table.Columns.Count, [], []),
        };
    }
}
