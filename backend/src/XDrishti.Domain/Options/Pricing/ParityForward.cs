namespace XDrishti.Domain.Options.Pricing;

/// <summary>
/// The forward by put-call parity (ADR 0178): <c>F = K0 + (Cmid - Pmid) / DF</c> at the strike K0 nearest the underlying among strikes where both sides have a bid and an ask, not crossed; ties take the lower strike.
/// Without such a strike the forward is <c>no_two_sided_quote</c>; a caller may then use <see cref="FromRate"/> only with the label <see cref="ForwardSource.ForwardFromRate"/>.
/// </summary>
public static class ParityForward
{
    public static PricedValue<Forward> From(IReadOnlyList<StrikeQuotes> quotes, double underlying, double discount)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        if (!double.IsFinite(underlying) || underlying <= 0)
        {
            return Priced.Unavailable<Forward>(UnavailableReason.BadInput);
        }

        if (!double.IsFinite(discount) || discount <= 0 || discount > 1)
        {
            return Priced.Unavailable<Forward>(UnavailableReason.BadRate);
        }

        StrikeQuotes? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var q in quotes)
        {
            if (!q.TwoSided || !double.IsFinite(q.Strike) || q.Strike <= 0)
            {
                continue;
            }

            var distance = Math.Abs(q.Strike - underlying);
            if (distance < bestDistance || (distance == bestDistance && q.Strike < best!.Value.Strike))
            {
                best = q;
                bestDistance = distance;
            }
        }

        if (best is not { } k0)
        {
            return Priced.Unavailable<Forward>(UnavailableReason.NoTwoSidedQuote);
        }

        var callMid = (k0.CallBid!.Value + k0.CallAsk!.Value) / 2.0;
        var putMid = (k0.PutBid!.Value + k0.PutAsk!.Value) / 2.0;
        var forward = k0.Strike + ((callMid - putMid) / discount);
        return double.IsFinite(forward) && forward > 0 ? Priced.Of(new Forward(forward, ForwardSource.Parity)) : Priced.Unavailable<Forward>(UnavailableReason.BadInput);
    }

    /// <summary>The rate-only forward <c>S e^(rT)</c>; the caller must carry <see cref="ForwardSource.ForwardFromRate"/> with it.</summary>
    public static PricedValue<Forward> FromRate(double underlying, double rate, double years)
    {
        if (!double.IsFinite(underlying) || underlying <= 0 || !double.IsFinite(years))
        {
            return Priced.Unavailable<Forward>(UnavailableReason.BadInput);
        }

        if (years <= 0)
        {
            return Priced.Unavailable<Forward>(UnavailableReason.Expired);
        }

        if (!double.IsFinite(rate) || rate < 0)
        {
            return Priced.Unavailable<Forward>(UnavailableReason.BadRate);
        }

        var forward = underlying * Math.Exp(rate * years);
        return double.IsFinite(forward) ? Priced.Of(new Forward(forward, ForwardSource.ForwardFromRate)) : Priced.Unavailable<Forward>(UnavailableReason.BadInput);
    }
}
