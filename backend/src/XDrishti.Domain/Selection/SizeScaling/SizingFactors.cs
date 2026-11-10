namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>The down factors of a quantity, combined (spec BK-10, decision 3): the smaller of the volatility scale and the gate scale, never above 1. Pure.</summary>
public static class SizingFactors
{
    /// <summary>
    /// <c>min(volatilityScale, gateScale)</c>; a missing (null or not finite) volatility scale does not scale (the gate scale stands). The result is in (0, 1]: a gate scale above 1 or not usable is taken as 1.
    /// </summary>
    public static double Combine(double? volatilityScale, double gateScale)
    {
        var gate = double.IsFinite(gateScale) && gateScale > 0 ? Math.Min(gateScale, 1.0) : 1.0;
        return volatilityScale is { } v && double.IsFinite(v) && v > 0 ? Math.Min(Math.Min(v, VolTarget.Ceiling), gate) : gate;
    }

    /// <summary><c>floor(fixedQuantity * factor)</c>, never above <paramref name="fixedQuantity"/> and never below 0.</summary>
    public static long Quantity(long fixedQuantity, double factor)
    {
        if (fixedQuantity <= 0 || !double.IsFinite(factor) || factor <= 0)
        {
            return 0;
        }

        return Math.Min(fixedQuantity, (long)Math.Floor(fixedQuantity * Math.Min(factor, 1.0)));
    }
}
