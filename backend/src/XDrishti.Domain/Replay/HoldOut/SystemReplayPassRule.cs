namespace XDrishti.Domain.Replay.HoldOut;

/// <summary>
/// The pass rule of the pre-registered system replay (SR-03 D3), fixed at registration and stored with it. All figures it judges are net of the checked charge sheet.
/// </summary>
/// <param name="MinTrades">The fewest closed trades of the selection arm for a verdict; fewer is <c>inconclusive</c> (default 30).</param>
/// <param name="MaxDrawdownPct">The largest maximum drawdown, in percent, a pass allows (default 15).</param>
public sealed record SystemReplayPassRule(int MinTrades = 30, double MaxDrawdownPct = 15)
{
    /// <summary>The rule of the owner's approval of 2026-11-13: 30 trades, 15 % drawdown, CAGR above 0, the arm comparison not <c>baseline_better</c>.</summary>
    public static SystemReplayPassRule Default { get; } = new();

    /// <summary>The problems of the values; empty when usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        if (MinTrades is < 1 or > 10_000)
        {
            p.Add("minTrades must be from 1 to 10000");
        }

        if (!double.IsFinite(MaxDrawdownPct) || MaxDrawdownPct is <= 0 or > 100)
        {
            p.Add("maxDrawdownPct must be above 0 and at most 100");
        }

        return p;
    }
}
