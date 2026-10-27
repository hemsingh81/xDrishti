using System.Text.Json.Nodes;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary>What a tool body hands to <see cref="ToolRunner"/>: raw rows and the facts around them. The runner enforces every limit and cleaning rule, so a tool cannot forget one.</summary>
internal sealed class ToolOutcome
{
    public IReadOnlyList<ToolColumn> Columns { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }

    public JsonObject? Data { get; init; }

    public int? TotalRows { get; init; }

    public bool Truncated { get; init; }

    public ToolEvidence? Evidence { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public ToolError? Error { get; init; }

    /// <summary>Leave null cells out of the rows (a catalog with an optional note); never for query results, where a null is an answer.</summary>
    public bool OmitNullCells { get; init; }

    /// <summary>Set by <c>run_sql</c> for a refused query: the reason word for the log (never the text).</summary>
    public string? RejectedReason { get; init; }

    public static ToolOutcome Fail(string code, string message, string? rejectedReason = null) => new() { Error = new ToolError(code, message), RejectedReason = rejectedReason };
}
