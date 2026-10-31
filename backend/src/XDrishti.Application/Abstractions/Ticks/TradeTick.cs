namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>
/// One stored row of <c>market.trade_ticks</c> (P7-16b criterion 4): the latest-trade fields of a quote packet whose cumulative volume rose (or a baseline). <see cref="Ts"/> is the receive time, <see cref="RxMonoMs"/> the monotonic milliseconds
/// since the run started, <see cref="RxSeq"/> the per-run packet counter (the only order within a millisecond: Dhan sends no sequence), <see cref="LttEpoch"/> the exchange trade time exactly as sent (int32 epoch, zone UNVERIFIED, not converted),
/// <see cref="LtpPx"/> the last price in integer paise, <see cref="Ltq"/> the last quantity, <see cref="CumVol"/> the day's cumulative volume, <see cref="Flags"/> the bits of <see cref="TickFlags"/>. No aggressor, side, bid or ask.
/// </summary>
public sealed record TradeTick(DateTimeOffset Ts, Guid InstrumentId, long RxMonoMs, long RxSeq, int LttEpoch, int LtpPx, int Ltq, long CumVol, short Flags);

/// <summary>The bits of <see cref="TradeTick.Flags"/> (the column allows 0 to 15).</summary>
public static class TickFlags
{
    /// <summary>The first row of an instrument in a run or after a reconnect: its volume step is not one instant's trade.</summary>
    public const short Baseline = 1;

    /// <summary>The last quantity was 65,535 (the most a uint16 holds): it may have been larger.</summary>
    public const short LtqCapped = 2;

    /// <summary>The cumulative volume is below the last stored one.</summary>
    public const short VolumeDown = 4;

    /// <summary>The exchange trade time is earlier than the last stored one.</summary>
    public const short LttBack = 8;
}
