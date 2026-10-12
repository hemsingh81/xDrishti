namespace XDrishti.Domain.Market.Actions;

/// <summary>An applied corporate action as the adjustment needs it: when the ex-date is, what it is and the price factor for the bars before it.</summary>
public sealed record AppliedAction(DateOnly ExDate, CorporateActionKind Kind, int Numerator, int Denominator, decimal Factor)
{
    public string Ratio => CorporateActionRatio.Text(Numerator, Denominator);
}
