namespace XDrishti.Domain.Filings;

/// <summary>One data line of the labelling file: the six text columns as the owner sees them and the label cell as typed.</summary>
public sealed record LabelLine(int Line, string Id, string FilingId, string Symbol, string PublishedAt, string Category, string Subject, string Text, string Label);
