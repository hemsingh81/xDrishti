using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary><c>list_tables</c> (spec P9-02 AC 9): the views you may query, from the reviewed manifest joined to the live database.</summary>
internal sealed class ListTablesTool(ToolRunner runner, IAiCatalog catalog) : ReadOnlyTool(Spec, runner)
{
    public static readonly ToolSpec Spec = new(
        "list_tables",
        "Lists the views you may query with run_sql (name, what a row is, class C1 or C2, approximate rows). Class C2 holds the owner's portfolio data and is empty while the portfolio context is off. Read-only.",
        new ArgDef("prefix", ArgType.String, false, "Only views whose name starts with this text.", 1, 40, "^[a-z0-9_]+$"));

    private static readonly ToolColumn[] Columns = [new("name", false), new("description", false), new("grain", false), new("classes", false), new("approxRows", false), new("note", false)];

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var prefix = arguments.String("prefix");
        var views = await catalog.ListAsync(prefix is null ? [] : [prefix], cancellationToken);
        var notes = views.Any(v => v.Note is not null) ? new List<string> { "Portfolio context is off: class C2 views return no rows." } : [];
        return new ToolOutcome
        {
            Columns = Columns,
            OmitNullCells = true, // a note is written only on the views that have one: a column of nulls would cost about 400 of the 6,000 characters
            Rows = [.. views.Select(v => (IReadOnlyList<object?>)[v.Name, v.Description, v.Grain, v.Classes, v.ApproxRows, v.Note])],
            Notes = notes,
            Evidence = new ToolEvidence("the reviewed list of readable views (schema aiv)", ["aiv"], null, views.Count, ["approxRows is the planner's estimate of the base table, not a count"], []),
        };
    }
}
