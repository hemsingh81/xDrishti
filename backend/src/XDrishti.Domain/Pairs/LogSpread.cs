namespace XDrishti.Domain.Pairs;

/// <summary>The log-price spread <c>s_t = ln y_t - alpha - beta ln x_t</c> and the one place prices become logs.</summary>
public static class LogSpread
{
    /// <summary>The natural logs of a price series; any non-finite or non-positive price is <c>bad_input</c>.</summary>
    public static PairsResult<double[]> Ln(ReadOnlySpan<double> prices)
    {
        var logs = new double[prices.Length];
        for (var i = 0; i < prices.Length; i++)
        {
            var p = prices[i];
            if (!double.IsFinite(p) || p <= 0)
            {
                return PairsResults.Unavailable<double[]>(PairsReason.BadInput);
            }

            logs[i] = Math.Log(p);
        }

        return PairsResults.Of(logs);
    }

    /// <summary>The spread of two log series at a given hedge ratio (for example the formation <c>alpha</c> and <c>beta</c> applied to later bars); unequal lengths is <c>too_short</c> and a non-finite number <c>bad_input</c>.</summary>
    public static PairsResult<double[]> Of(ReadOnlySpan<double> lnY, ReadOnlySpan<double> lnX, double alpha, double beta)
    {
        if (lnY.Length != lnX.Length)
        {
            return PairsResults.Unavailable<double[]>(PairsReason.TooShort);
        }

        if (!double.IsFinite(alpha) || !double.IsFinite(beta) || !HedgeRatio.AllFinite(lnY) || !HedgeRatio.AllFinite(lnX))
        {
            return PairsResults.Unavailable<double[]>(PairsReason.BadInput);
        }

        var spread = new double[lnY.Length];
        for (var i = 0; i < spread.Length; i++)
        {
            spread[i] = lnY[i] - alpha - (beta * lnX[i]);
        }

        return HedgeRatio.AllFinite(spread) ? PairsResults.Of(spread) : PairsResults.Unavailable<double[]>(PairsReason.BadInput);
    }
}
