namespace XDrishti.Domain.Reporting;

/// <summary>One coordinate of a group: the dimension and its value. A null value is "not recorded" and makes every figure of the group unavailable.</summary>
/// <param name="Dimension">The dimension.</param>
/// <param name="Value">The value; null when the run or trade did not record it.</param>
public sealed record GroupPart(ReportDimension Dimension, string? Value)
{
    /// <summary>The text shown for the value.</summary>
    public string Text => Value ?? "(not recorded)";
}
