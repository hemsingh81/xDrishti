using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The chain of hold-out blocks (HO-01, ADR 0283): block 1 is the stored block as it is, block k+1 is <c>[end_k + 1 day, end_k + L months]</c> with L read when that block is stored. Pure date arithmetic; a stored block never changes, so the chain has no gaps and no overlaps.
/// </summary>
public static class HoldOutChain
{
    /// <summary>The smallest <c>Confidence:HoldOut:BlockMonths</c> (the CF-06 D4 minimum).</summary>
    public const int MinBlockMonths = 3;

    /// <summary>The largest <c>Confidence:HoldOut:BlockMonths</c>.</summary>
    public const int MaxBlockMonths = 12;

    /// <summary>The block after one that ends on <paramref name="previousEnd"/>: the day after it up to <paramref name="months"/> months after that end. 2026-11-04 with 6 months gives 2026-11-05 to 2027-05-04.</summary>
    public static (DateOnly Start, DateOnly End) Next(DateOnly previousEnd, int months)
    {
        if (months is < MinBlockMonths or > MaxBlockMonths)
        {
            throw new ArgumentOutOfRangeException(nameof(months), months, $"A block is {MinBlockMonths} to {MaxBlockMonths} months.");
        }

        return (previousEnd.AddDays(1), previousEnd.AddMonths(months));
    }

    /// <summary>Whether the block ending on <paramref name="end"/> is complete: no trading day is left after the last closed session up to its end. A block whose end is a holiday seals at the close of its last session.</summary>
    public static bool IsSealed(TradingCalendar calendar, DateOnly lastClosedSession, DateOnly end)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return calendar.TradingDaysBetween(lastClosedSession, end) == 0;
    }

    /// <summary>The state of a block from what is known of it. Released wins, then <c>filling</c> (an unsealed block has no valid look), then looked, then sealed.</summary>
    public static HoldOutBlockState StateOf(bool released, bool sealedBlock, bool freshLook) =>
        released ? HoldOutBlockState.Released
        : !sealedBlock ? HoldOutBlockState.Filling
        : freshLook ? HoldOutBlockState.Looked
        : HoldOutBlockState.Sealed;

    /// <summary>The wire word of a state (<c>filling</c>, <c>sealed</c>, <c>looked</c>, <c>released</c>).</summary>
    public static string Word(HoldOutBlockState state) => state switch
    {
        HoldOutBlockState.Filling => "filling",
        HoldOutBlockState.Sealed => "sealed",
        HoldOutBlockState.Looked => "looked",
        HoldOutBlockState.Released => "released",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
