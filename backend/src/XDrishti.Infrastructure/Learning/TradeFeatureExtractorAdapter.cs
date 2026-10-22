using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// <see cref="ITradeFeatureExtractor"/> over the simulation library (spec P6-04, ADR 0066, ADR 0039): thin, stateless, no I/O or clock. No market context is passed (the daily context is not loaded until P3-01d), so the context features are unknown (NaN).
/// </summary>
internal sealed class TradeFeatureExtractorAdapter : ITradeFeatureExtractor
{
    public IReadOnlyDictionary<string, double[]> Extract(IReadOnlyList<Bar> rawBars, AdjustmentFactors factors, TradingCalendar calendar, IReadOnlyList<TradeFeatureDecision> decisions) =>
        TradeFeatureExtractor.Extract(rawBars, factors, calendar, decisions);
}
