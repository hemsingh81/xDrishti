namespace XDrishti.Application.Reporting;

/// <summary>One coordinate of a group: the dimension (lower case) and its value; a null value is "not recorded".</summary>
/// <param name="Dimension">The dimension name.</param>
/// <param name="Value">The value; null when the run or trade did not record it.</param>
public sealed record GroupPartView(string Dimension, string? Value);
