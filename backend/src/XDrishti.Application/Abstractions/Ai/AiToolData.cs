using System.Text.Json.Nodes;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// What a read port of the assistant's tools hands back (spec P9-05): rows and/or a data object, the evidence and notes, or an error of the tool contract. The tool server turns it into the envelope; every limit and the cleaning of text are applied there, not here.
/// </summary>
public sealed record AiToolData(
    IReadOnlyList<AiToolColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>>? Rows,
    JsonObject? Data,
    int? TotalRows,
    bool Truncated,
    ToolEvidence? Evidence,
    IReadOnlyList<string> Notes,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    /// <summary>A failed call: the code and a fixed message (never an argument value).</summary>
    public static AiToolData Fail(string code, string message, IReadOnlyList<string>? notes = null) => new([], null, null, null, false, null, notes ?? [], code, message);

    /// <summary>An answer made of a data object only.</summary>
    public static AiToolData OfData(JsonObject data, ToolEvidence evidence, IReadOnlyList<string>? notes = null) => new([], null, data, null, false, evidence, notes ?? []);

    /// <summary>An answer made of rows.</summary>
    public static AiToolData OfRows(IReadOnlyList<AiToolColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, ToolEvidence evidence, IReadOnlyList<string>? notes = null, JsonObject? data = null, int? totalRows = null, bool truncated = false) =>
        new(columns, rows, data, totalRows, truncated, evidence, notes ?? []);
}
