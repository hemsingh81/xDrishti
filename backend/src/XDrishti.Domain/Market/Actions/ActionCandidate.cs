namespace XDrishti.Domain.Market.Actions;

/// <summary>A suspected corporate action found in the daily bars: the ex-date (the day the price fell), the suggested kind and ratio and a note that says what was seen and names the alternative reading.</summary>
public sealed record ActionCandidate(DateOnly ExDate, CorporateActionKind Kind, int Numerator, int Denominator, string Note);
