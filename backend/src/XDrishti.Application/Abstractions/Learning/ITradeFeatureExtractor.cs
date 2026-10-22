using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The decision-time features of trades, computed from bars (P6-04, ADR 0066): a port in front of the feature frame, which is pure calculation behind an adapter in Infrastructure (ADR 0039).</summary>
public interface ITradeFeatureExtractor
{
    /// <summary>
    /// The stored values (<see cref="TradeFeatureCatalog.StoredCount"/>, catalog order, NaN where unknown) by trade key. Only bars that start before a decision's instant are read for it. Pure and synchronous.
    /// </summary>
    /// <param name="rawBars">The instrument's stored 1-minute bars, raw, ascending.</param>
    /// <param name="factors">Its applied corporate actions.</param>
    /// <param name="calendar">The trading calendar.</param>
    /// <param name="decisions">The trades of the instrument.</param>
    IReadOnlyDictionary<string, double[]> Extract(IReadOnlyList<Bar> rawBars, AdjustmentFactors factors, TradingCalendar calendar, IReadOnlyList<TradeFeatureDecision> decisions);
}
