namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One computed value of an extension row: the number (NaN = unknown), the closed reason when it is unknown, and whether it was built on facts of a parser that has not passed real filings (labelled, counted, never silently trusted).</summary>
public readonly record struct FeatureCell(double Value, string? Reason, bool Unverified)
{
    public bool IsKnown => !double.IsNaN(Value);

    public static FeatureCell Known(double value, bool unverified = false) => new(value, null, unverified);

    public static FeatureCell Unknown(string reason) => new(double.NaN, reason, false);
}
