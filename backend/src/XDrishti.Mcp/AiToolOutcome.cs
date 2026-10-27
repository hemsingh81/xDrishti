using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Mcp;

/// <summary>Turns what a read port answered into the outcome the runner shapes: the port's error code stays the tool's error code, its rows keep their text/structured flag per column.</summary>
internal static class AiToolOutcome
{
    public static ToolOutcome From(AiToolData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.ErrorCode is not null
            ? ToolOutcome.Fail(data.ErrorCode, data.ErrorMessage ?? "The tool could not answer.")
            : new ToolOutcome
            {
                Columns = [.. data.Columns.Select(c => new ToolColumn(c.Name, c.IsText))],
                Rows = data.Rows,
                Data = data.Data,
                TotalRows = data.TotalRows,
                Truncated = data.Truncated,
                Evidence = data.Evidence,
                Notes = data.Notes,
            };
    }
}
