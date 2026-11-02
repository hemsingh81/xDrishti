namespace XDrishti.Domain.Filings;

/// <summary>A row chosen for the labelled set, with its definite label (<c>material</c> or <c>non_material</c>).</summary>
public sealed record LabelledPick(SampleFileRow Row, string Label);
