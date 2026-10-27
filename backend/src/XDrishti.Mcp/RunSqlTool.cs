using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary>
/// <c>run_sql</c> (spec P9-02 AC 8): one SELECT over the <c>aiv</c> views. The pre-check only gives readable errors; the safety is the privilege of the database role, the read-only transaction and the cursor (see <see cref="IAiSqlRunner"/>). An answer that rests on this tool alone says that
/// it was not checked by the report engine.
/// </summary>
internal sealed partial class RunSqlTool(ToolRunner runner, IAiSqlRunner sql, IOptions<McpOptions> options) : ReadOnlyTool(Spec, runner)
{
    public const string Basis = "ad-hoc query, not checked by the report engine";

    public static readonly ToolSpec Spec = new(
        "run_sql",
        "Runs one read-only SELECT (or WITH ... SELECT) over the views of list_tables and returns the rows. One statement, no comments, at most 4000 characters, filter by date or symbol. The answer is not checked by the report engine. Read-only.",
        new ArgDef("sql", ArgType.String, true, "The query text.", 1, ToolLimits.MaxSqlChars),
        new ArgDef("maxRows", ArgType.Integer, false, "Rows to return, 1 to 200 (default 50).", 1, ToolLimits.MaxRows));

    private static readonly string[] StringTypes = ["text", "character varying", "varchar", "character", "bpchar", "char", "name", "citext", "unknown"];

    protected override async Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var text = arguments.String("sql")!;
        var check = SqlPreCheck.Check(text);
        if (!check.Accepted)
        {
            return ToolOutcome.Fail(ToolErrorCodes.SqlRejected, $"The query was refused ({check.Reason}). Send one SELECT over the views of list_tables, no comments, no semicolon inside.", check.Reason);
        }

        var maxRows = arguments.Integer("maxRows") ?? ToolLimits.DefaultRows;
        var outcome = await sql.RunAsync(check.Statement, maxRows, TimeSpan.FromSeconds(options.Value.SqlTimeoutSeconds), cancellationToken);
        switch (outcome.Status)
        {
            case SqlRunStatus.Denied:
                return ToolOutcome.Fail(ToolErrorCodes.SqlDenied, "The query uses something the read-only role may not read; only the views of list_tables can be queried.");
            case SqlRunStatus.Timeout:
                return ToolOutcome.Fail(ToolErrorCodes.SqlTimeout, $"The query took longer than {options.Value.SqlTimeoutSeconds} seconds; filter by date or symbol.");
            case SqlRunStatus.Error:
                return ToolOutcome.Fail(ToolErrorCodes.SqlError, "The database refused the query: " + outcome.ErrorText);
            case SqlRunStatus.Unavailable:
                throw new AiDatabaseUnavailableException();
        }

        var columns = new List<ToolColumn>(outcome.Columns.Count);
        for (var i = 0; i < outcome.Columns.Count; i++)
        {
            columns.Add(new ToolColumn(outcome.Columns[i].Name, IsText(outcome.Columns[i], outcome.Rows, i)));
        }

        return new ToolOutcome
        {
            Columns = columns,
            Rows = outcome.Rows,
            Truncated = outcome.Truncated,
            Data = new JsonObject
            {
                ["columns"] = new JsonArray([.. outcome.Columns.Select(c => (JsonNode?)new JsonObject { ["name"] = c.Name, ["type"] = c.DbType })]),
                ["elapsedMs"] = outcome.ElapsedMs,
            },
            Notes = outcome.Truncated ? [$"More rows exist than the {outcome.Rows.Count} returned; add a filter or aggregate."] : [],
            Evidence = new ToolEvidence(Basis, ["aiv"], null, outcome.Rows.Count, ["the views inherit the limits of the stored data (modelled fills, in-sample cells)"], [], SqlPreCheck.Hash(text)),
        };
    }

    /// <summary>
    /// Whether the cells of an output column are free text. The manifest decides by the column's name (a name that is structured in every view is structured); a name the manifest does not know (an alias, an aggregate) is text when the database type is a string type, except a column whose
    /// every cell is the shape of a SHA-256 (64 lower-case hex), which is a hash under another name and is never cut or redacted.
    /// </summary>
    private static bool IsText(SqlColumn column, IReadOnlyList<IReadOnlyList<object?>> rows, int index)
    {
        var type = TypeModifier().Replace(column.DbType, string.Empty).Replace("[]", string.Empty, StringComparison.Ordinal).TrimStart('_').Trim();
        if (!StringTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (DataManifest.KindOfOutput(column.Name) != ColumnKind.Text)
        {
            return false;
        }

        var cells = rows.Select(r => r[index]).OfType<string>().ToList();
        return cells.Count == 0 || !cells.All(c => Sha256Shape().IsMatch(c));
    }

    [GeneratedRegex(@"\(.*?\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TypeModifier();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sha256Shape();
}
