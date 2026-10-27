namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>
/// A number read from text. <paramref name="Value"/> is the absolute value (sign is ignored by the check, by design: "-0.3R" matches a source -0.3 and "lost 0.3R" matches its magnitude), already multiplied by k, L or Cr. <paramref name="HalfUnit"/> is half of the last printed digit
/// (0.005 for "0.12", 500 for "5k"). <paramref name="IsPlainInteger"/>: printed with no decimals, no unit and no percent sign (a count: it must match exactly, or match a source with decimals after rounding).
/// </summary>
public sealed record ParsedNumber(decimal Value, decimal HalfUnit, bool IsPercent, bool IsPlainInteger);
