namespace XDrishti.Domain.Options.Remark;

/// <summary>The net rupees of a re-marked trade, or the fate that says why there are none, with the labels of the charges and the spread used.</summary>
public sealed record NetOutcome(decimal? NetInr, string? Fate, IReadOnlyList<string> Labels);
