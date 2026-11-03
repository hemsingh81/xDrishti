namespace XDrishti.Domain.Options.Costs;

/// <summary>The three answers of the liquidity filter. <see cref="Unknown"/> is the first member: an uninitialised verdict is never a pass.</summary>
public enum LiquidityVerdict
{
    Unknown,
    Filtered,
    Pass,
}
