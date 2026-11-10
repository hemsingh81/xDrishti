namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>The most shares a suggestion may be, as a share of the 20-session median traded value (spec BK-10). Pure; a figure that cannot be computed is null (unavailable), never a pass and never a made-up cap.</summary>
public static class CapacityCap
{
    /// <summary><c>floor(pct * adv20 / entry)</c>; null when any input is not a positive finite number (or the percentage is out of range).</summary>
    public static long? Quantity(double? adv20, double? entry, double pct)
    {
        if (adv20 is not { } adv || entry is not { } price || !double.IsFinite(adv) || !double.IsFinite(price) || adv <= 0 || price <= 0 || !SizingOptions.CapacityPctIsValid(pct))
        {
            return null;
        }

        var shares = Math.Floor(pct * adv / price);
        return shares >= long.MaxValue ? long.MaxValue : (long)shares;
    }
}
