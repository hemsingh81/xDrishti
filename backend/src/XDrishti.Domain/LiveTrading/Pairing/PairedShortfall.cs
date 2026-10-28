namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The same ticket on both sides, in R on the common risk per share rho (the plan's |entry - stop|; spec P11-03): <see cref="ShortfallR"/> = paper R - live R (positive = live worse) = <see cref="EntryR"/> + <see cref="ExitR"/>, exactly (decimal arithmetic).
/// </summary>
public sealed record PairedShortfall(double PaperR, double LiveR, double EntryR, double ExitR)
{
    public double ShortfallR => EntryR + ExitR;

    /// <summary>The split of one pair; null when rho is not positive. d = +1 for a buy, -1 for a sell.</summary>
    public static PairedShortfall? Pair(bool buy, decimal liveEntry, decimal liveExit, decimal paperEntry, decimal paperExit, decimal rho)
    {
        if (rho <= 0)
        {
            return null;
        }

        var d = buy ? 1m : -1m;
        return new PairedShortfall(
            (double)(d * (paperExit - paperEntry) / rho),
            (double)(d * (liveExit - liveEntry) / rho),
            (double)(d * (liveEntry - paperEntry) / rho),
            (double)(d * (paperExit - liveExit) / rho));
    }
}
