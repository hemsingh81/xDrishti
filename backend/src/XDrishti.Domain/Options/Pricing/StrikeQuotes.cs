namespace XDrishti.Domain.Options.Pricing;

/// <summary>The call and put bid and ask at one strike, in rupees; a side the vendor did not send is null.</summary>
public readonly record struct StrikeQuotes(double Strike, double? CallBid, double? CallAsk, double? PutBid, double? PutAsk)
{
    /// <summary>Both sides have a bid and an ask above zero and neither is crossed.</summary>
    public bool TwoSided => Side(CallBid, CallAsk) && Side(PutBid, PutAsk);

    private static bool Side(double? bid, double? ask) => bid is { } b && ask is { } a && double.IsFinite(b) && double.IsFinite(a) && b > 0 && a > 0 && b <= a;
}
