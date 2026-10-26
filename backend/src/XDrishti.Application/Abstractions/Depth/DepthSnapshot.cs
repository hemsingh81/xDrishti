namespace XDrishti.Application.Abstractions.Depth;

/// <summary>
/// One stored row of <c>market.depth_snapshots</c> (P7-16): the whole 20-level book of an instrument at the moment of receipt. Prices are integer paise, quantities shares; a level beyond the book is 0/0/0. <see cref="Seq"/> is the bid packet's
/// sequence (meaning UNVERIFIED), <see cref="RxMonoMs"/> the monotonic milliseconds since the run started (a wall-clock jump shows against <see cref="Ts"/>), <see cref="Flags"/> the bits of <see cref="DepthFlags"/>.
/// </summary>
public sealed record DepthSnapshot(DateTimeOffset Ts, Guid InstrumentId, int Seq, long RxMonoMs, short Flags, int[] BidPx, int[] BidQty, int[] BidOrd, int[] AskPx, int[] AskQty, int[] AskOrd);

/// <summary>The bits of <see cref="DepthSnapshot.Flags"/> (the column allows 0 to 15).</summary>
public static class DepthFlags
{
    /// <summary>Best bid is at or above best ask.</summary>
    public const short Crossed = 1;

    /// <summary>A quantity or an order count was above <see cref="int.MaxValue"/> and was clamped.</summary>
    public const short QuantityClamped = 2;

    /// <summary>One side was older than five seconds when the row was made.</summary>
    public const short StaleSide = 4;

    /// <summary>A side is not in price order.</summary>
    public const short UnorderedLadder = 8;
}
