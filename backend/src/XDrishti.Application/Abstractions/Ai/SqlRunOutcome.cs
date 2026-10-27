namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The result of one ad-hoc query. Values are already plain: null, bool, long, double, string (numeric with its scale kept, times as ISO UTC) or lists of those.</summary>
public sealed record SqlRunOutcome(SqlRunStatus Status, IReadOnlyList<SqlColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, bool Truncated, long ElapsedMs, string? ErrorText)
{
    public static SqlRunOutcome Failed(SqlRunStatus status, long elapsedMs, string? errorText = null) => new(status, [], [], false, elapsedMs, errorText);
}
