using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// S1 to S3 and S5: the slippage of market-type fills on the price grid of the instrument, in decimal. <paramref name="TierBps"/> (CF-05, D2) is the tiered option: null is the default rule; a number makes the amount
/// <c>max(Ticks × tick, TierBps/10000 × price) + extra × tick + BarRangeFrac × range</c>.
/// </summary>
internal readonly record struct Slip(bool Modelled, decimal Ticks, decimal Extra, decimal Fraction, decimal Tick, decimal? TierBps = null)
{
    private const decimal BpsToFraction = 10_000m;

    public static Slip Of(SlippageConfig config, decimal tick, decimal? tierBps = null)
    {
        _ = TickGrid.TryConvert(config.Ticks, out var ticks);
        _ = TickGrid.TryConvert(config.StopExitExtraTicks, out var extra);
        _ = TickGrid.TryConvert(config.BarRangeFrac, out var fraction);
        return new Slip(!config.IsNone, ticks, extra, fraction, tick, tierBps);
    }

    /// <summary>S2: <c>(Ticks + extra) × tick + BarRangeFrac × range</c> of the minute the fill happens in; the extra ticks are for stop exits. With a tier: <c>max(Ticks × tick, bps × price) + extra × tick + BarRangeFrac × range</c>.</summary>
    private decimal Amount(decimal price, decimal range, bool stopExit)
    {
        if (TierBps is not { } bps)
        {
            return ((Ticks + (stopExit ? Extra : 0m)) * Tick) + (Fraction * range);
        }

        return Math.Max(Ticks * Tick, bps / BpsToFraction * price) + ((stopExit ? Extra : 0m) * Tick) + (Fraction * range);
    }

    /// <summary>S3 for an entry at the open: against the trade, on the grid, never past the limit. With no slippage the entry does not move.</summary>
    public decimal Entry(bool isBuy, decimal entry, decimal limit, decimal range)
    {
        if (!Modelled)
        {
            return entry;
        }

        var amount = Amount(entry, range, stopExit: false);
        return isBuy ? Math.Min(TickGrid.Up(entry + amount, Tick), limit) : Math.Max(TickGrid.Down(entry - amount, Tick), limit);
    }

    /// <summary>
    /// S1, S3 and S5 for an exit: a target is a resting limit and only goes on the grid; every other exit (a stop with the extra ticks, the square-off, <c>MaxHold</c> and <c>EndOfData</c> without) slips against the trade.
    /// With no slippage nothing moves.
    /// </summary>
    public decimal Exit(bool isBuy, ExitReason reason, decimal exit, decimal range)
    {
        if (!Modelled)
        {
            return exit;
        }

        var amount = reason == ExitReason.Target ? 0m : Amount(exit, range, stopExit: reason == ExitReason.Stop);
        return isBuy ? TickGrid.Down(exit - amount, Tick) : TickGrid.Up(exit + amount, Tick);
    }
}
