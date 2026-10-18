using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Research;

/// <summary>An applied corporate action that changed the bars of a replay (ex-date after the first bar read). <see cref="OnCsvBars"/> marks an action whose ex-date is on or before the date the loaded CSV files reach (the comparison of the double-adjustment guard).</summary>
public sealed record AppliedAdjustment(DateOnly ExDate, CorporateActionKind Kind, int Numerator, int Denominator, decimal Factor, bool OnCsvBars);
