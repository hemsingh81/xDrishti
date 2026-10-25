using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The hand-worked fixture of spec P10-01: BUY ABC, replay fill minute 09:20 on 2026-11-03 at the exact entry 100.10 (filled 100.15), stop 99.00, target 102.30, exit minute 11:05 at the target (no slip on a target):
/// <c>RExact = 2.20 / 1.10 = 2.0</c>, <c>RFilled = 2.15 / 1.10 = 1.954545</c>. A charge sheet of a flat 10.00 per order and nothing else is built here and never stored.
/// </summary>
internal static class PaperFixture
{
    public static readonly DateOnly Session = new(2026, 11, 3);

    public const string SheetId = "flat-10";

    public static readonly double RFilled = 2.15 / 1.10;

    /// <summary>A flat 10.00 per order, no tax, no GST, from 2026-01-01.</summary>
    public static CostSheet FlatSheet { get; } = TestCostSheet.Of(
        SheetId,
        TestCostSheet.Schedule(new DateOnly(2026, 1, 1), TestCostSheet.Rates(0m, 10m, 10m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), TestCostSheet.Rates(0m, 10m, 10m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), 0m));

    /// <summary>The instant of the fill minute, 2026-11-03 09:20:00 IST.</summary>
    public static long EntryEpoch { get; } = new DateTimeOffset(2026, 11, 3, 9, 20, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    /// <summary>The closed trade of the fixture; <paramref name="netR"/> set means the replay charged it (its costing carries that net R).</summary>
    public static SimTrade Trade(Guid instrument, Side side = Side.Buy, double? netR = null, long? entryEpoch = null)
    {
        var entry = entryEpoch ?? EntryEpoch;
        var costing = netR is { } n ? new TradeCosting(CostSegment.IntradayEquity, SheetId, 100, 110m, 10015m, 10230m, LegCharges.Zero, LegCharges.Zero, 20m, 0.18, n) : null;
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, "ORB", instrument, "ABC", side, Session, entry - 60, Timeframe.M15, new Bar(entry - 960, 99, 100.5, 99, 100.1, 15000), 15, true,
            entry, 100.10, 99.00, 102.30, entry + (105 * 60), 102.30, ExitReason.Target, 2.0, 0.4, 2.1, FillBasis.Modelled, costing is null ? CostBasis.None : CostBasis.Charged, null,
            RFilled, RFilled - 2.0, 100.15, 102.30, EntryFill.AtOpen, false, 1, 0, costing);
    }
}
