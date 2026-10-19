using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The engine side of the cost comparison (spec P3-08c, written by E): the one place where the oracle's cost records (<see cref="OracleCostInput"/>, <see cref="OracleChargeSheet"/>, <see cref="OracleSizing"/>) and the engine's
/// (<see cref="SimTrade"/>, <see cref="CostSheet"/>, <see cref="SizingProfile"/>, <see cref="CostContext"/>) are mapped into each other, and the engine's answer is read back into the one view the comparator reads. Like
/// <see cref="OracleAdapter"/> it is held to account by round-trip tests with sentinels (<c>CostAdapterTests</c>), because a field it drops or normalises would hollow the agreement.
/// </summary>
internal static class CostAdapter
{
    /// <summary>The engine's own bound on the limit (<c>CostContext.MaxCostRLimit</c>, P3-03c D-criterion 10): a limit above it is not an engine input.</summary>
    internal const decimal EngineMaxCostR = CostContext.MaxCostRLimit;

    // ---- the sheet

    internal static OracleChargeSheet Sheet(CostSheet sheet) => new(sheet.Id, [.. sheet.Schedules.Select(Schedule)]);

    private static OracleSchedule Schedule(ChargeSchedule s) => new(s.EffectiveFrom, s.GstPct, Rounding(s.Rounding), Rates(s.IntradayEquity), Rates(s.Delivery));

    private static OracleRates Rates(ChargeRates r) => new(r.BrokeragePct, r.BrokerageCap, r.BrokerageMin, r.SttBuyPct, r.SttSellPct, r.ExchangeTxnPct, r.SebiPerCrore, r.StampBuyPct, r.OtherPct, r.DpChargePerSell);

    private static OracleRounding Rounding(ChargeRounding r) => r switch
    {
        ChargeRounding.HalfAwayFromZero => OracleRounding.HalfAwayFromZero,
        ChargeRounding.HalfEven => OracleRounding.HalfEven,
        ChargeRounding.Down => OracleRounding.Down,
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    private static ChargeRounding Rounding(OracleRounding r) => r switch
    {
        OracleRounding.HalfAwayFromZero => ChargeRounding.HalfAwayFromZero,
        OracleRounding.HalfEven => ChargeRounding.HalfEven,
        OracleRounding.Down => ChargeRounding.Down,
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>The engine's sheet for an oracle sheet. The id is lower-cased (the engine's id alphabet is lower case; the generator's ids carry the category name); every rate, date, rounding mode and GST figure is carried as it is.</summary>
    internal static CostSheet EngineSheet(OracleChargeSheet sheet)
    {
        var schedules = sheet.Schedules.Select(s => ChargeSchedule.Create(s.EffectiveFrom, s.GstPct, Rounding(s.Rounding), EngineRates(s.Intraday), EngineRates(s.Delivery)).Value).ToList();
        var created = CostSheet.Create(sheet.Id.ToLowerInvariant(), "oracle sheet (invented numbers)", "invented by the oracle's generator; not a broker's sheet", null, schedules);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code + " " + created.Error.Message : string.Empty);
        return created.Value;
    }

    private static ChargeRates EngineRates(OracleRates r)
    {
        var created = ChargeRates.Create(r.BrokeragePct, r.BrokerageCap, r.BrokerageMin, r.SttBuyPct, r.SttSellPct, r.ExchangePct, r.SebiPerCrore, r.StampBuyPct, r.OtherPct, r.DpChargePerSell);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code + " " + created.Error.Message : string.Empty);
        return created.Value;
    }

    // ---- the sizing, the segment, the side

    internal static SizingProfile Profile(OracleSizing s)
    {
        if (s == OracleSizing.Standard)
        {
            return SizingProfile.Standard;
        }

        if (s == OracleSizing.Conservative)
        {
            return SizingProfile.Conservative;
        }

        return SizingProfile.Create("oracle-profile", s.RiskPct, s.MaxCapitalPct).Value;
    }

    internal static OracleSizing Sizing(SizingProfile p) => new(p.RiskPerTradePct, p.MaxCapitalPerTradePct);

    internal static CostSegment Segment(OracleCostSegment s) => s == OracleCostSegment.Delivery ? CostSegment.Delivery : CostSegment.IntradayEquity;

    internal static OracleCostSegment Segment(CostSegment s) => s == CostSegment.Delivery ? OracleCostSegment.Delivery : OracleCostSegment.IntradayEquity;

    // ---- the trade

    /// <summary>
    /// The engine's gross trade for the oracle's cost input: the exact entry, the stop, the filled prices and the two dates become the double fields of a <see cref="SimTrade"/> (an 8-place decimal survives the cast and the engine's own
    /// 8-place rounding, so the engine reads back the same decimals); <c>RExact</c> and <c>RFilled</c> are the input's.
    /// </summary>
    internal static SimTrade EngineTrade(OracleCostInput t)
    {
        var side = t.Side == OracleSide.Buy ? Side.Buy : Side.Sell;
        var made = CostTrades.Make(
            side,
            t.EntryDate,
            (double)t.EntryExact,
            (double)t.StopPrice,
            (double)t.ExitFilled,
            entryFilled: (double)t.EntryFilled,
            exitFilled: (double)t.ExitFilled,
            exitDate: t.ExitDate == t.EntryDate ? null : t.ExitDate);
        return made with { RExact = t.RExact, RFilled = t.RFilled };
    }

    /// <summary>
    /// The oracle's cost input for a gross trade the walker gave (the chain of the spec, AC17): the costing reads the stored record, so each price is the stored double converted by the rule of ADR 0042 D3 and ADR 0047 R1 (the platform
    /// conversion, then 8 places, the midpoint to even), the entry date is the trade's date and the exit date the India date of the exit minute.
    /// </summary>
    internal static OracleCostInput Input(OracleTrade trade, OracleCostSegment segment, decimal capital, decimal maxCostR) =>
        new(
            trade.Side,
            Price(trade.EntryPrice),
            Price(trade.EntryFilledPrice),
            Price(trade.StopPrice),
            Price(trade.ExitFilledPrice),
            trade.RExact,
            trade.RFilled,
            trade.TradeDate,
            OracleClock.DateOf(trade.ExitEpochSeconds),
            segment,
            capital,
            maxCostR);

    private static decimal Price(double price) => Math.Round((decimal)price, 8, MidpointRounding.ToEven);

    // ---- the engine's answer

    /// <summary>The engine's costing of a trade: <c>CostContext</c> from the limit (clamped to the engine's bound; see <see cref="Clamped"/>), then <c>TradeCostApplier.Apply</c>.</summary>
    internal static Result<CostApplication> Apply(SimTrade trade, CostSheet sheet, SizingProfile profile, decimal capital, decimal maxCostR, CostSegment segment)
    {
        var context = CostContext.Create(sheet, profile, capital, Math.Min(maxCostR, EngineMaxCostR));
        context.IsSuccess.ShouldBeTrue(context.IsFailure ? context.Error.Code + " " + context.Error.Message : string.Empty);
        return TradeCostApplier.Apply(trade, context.Value, segment);
    }

    /// <summary>True when the limit the oracle was given is above what the engine accepts, so the adapter handed the engine its bound instead (the generator's 1000: log entry D-04).</summary>
    internal static bool Clamped(decimal maxCostR) => maxCostR > EngineMaxCostR;
}
