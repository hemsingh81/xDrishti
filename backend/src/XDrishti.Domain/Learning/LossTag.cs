namespace XDrishti.Domain.Learning;

/// <summary>One tag on one loss with the number that justifies it (P6-15): the measured value, the threshold it was held against, and a fixed sentence.</summary>
/// <param name="Kind">The tag.</param>
/// <param name="Value">The measured value (R, minutes, a fraction of price or a minute of the day, as the kind says).</param>
/// <param name="Threshold">The threshold the rule used.</param>
/// <param name="Detail">The justification in plain text.</param>
public sealed record LossTag(LossTagKind Kind, double Value, double Threshold, string Detail);
