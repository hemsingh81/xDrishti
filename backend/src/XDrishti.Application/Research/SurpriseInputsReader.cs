using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Research;

/// <summary>
/// Reads the surprise numbers of the filings of a run (P15-09) with the extractor of the extensions: for each filing that names a period, the features of <c>fu-1</c> (<c>sue_yoy</c>) and <c>ev-1</c> (<c>results_car3</c>) at the close of its reaction session, the first instant at which both are
/// computable. Every read is at or before that instant by the functions of the domain; nothing later is looked at. A filing whose reaction session the calendar cannot find is left out (it ranks nothing).
/// </summary>
internal sealed class SurpriseInputsReader(ExtensionFeatureExtractor extractor, IContextDayStore contextDays) : ISurpriseInputsReader
{
    private static readonly int SueIndex = FundamentalFeatureCatalog.Names.ToList().IndexOf("sue_yoy");

    private static readonly int CarIndex = EventFeatureCatalog.Names.ToList().IndexOf(EventFeatureCatalog.ResultsCar3);

    public async ValueTask<SurpriseInputs> ReadAsync(IReadOnlyList<ResultsFilingTime> filings, EventCalendarSnapshot snapshot, TradingCalendar calendar, TimeSpan lag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(calendar);
        var byInstrument = filings.GroupBy(f => f.InstrumentId).ToDictionary(g => g.Key, g => (IReadOnlyList<ResultsFilingTime>)g.ToList());
        var days = await contextDays.ListAsync(cancellationToken);
        var sources = new ExtensionSources(calendar, lag, snapshot, byInstrument, days.Count == 0 ? null : days.ToDictionary(d => d.Date, d => d.IndexClose));
        var observations = new List<SurpriseObservation>();
        foreach (var (instrument, list) in byInstrument.OrderBy(p => p.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decisions = new List<(ResultsFilingTime Filing, ExtensionDecision Decision)>();
            foreach (var filing in list.Where(f => f.PeriodEnd is not null))
            {
                if (ReactionSession.Of(filing.BroadcastTs, lag, calendar) is { } reaction && ReactionSession.CloseOf(reaction, calendar) is { } close)
                {
                    decisions.Add((filing, new ExtensionDecision(Key(filing), close)));
                }
            }

            if (decisions.Count == 0)
            {
                continue;
            }

            var only = decisions.Select(d => d.Decision).ToList();
            var fundamentals = await extractor.ExtractAsync(FundamentalFeatureCatalog.Version, instrument, only, sources, cancellationToken);
            var events = await extractor.ExtractAsync(EventFeatureCatalog.Version, instrument, only, sources, cancellationToken);
            foreach (var (filing, decision) in decisions)
            {
                var sue = fundamentals[decision.TradeKey][SueIndex];
                var car = events[decision.TradeKey][CarIndex];
                observations.Add(new SurpriseObservation(instrument, filing.PeriodEnd!.Value, filing.BroadcastTs, sue.Value, car.Value, sue.Unverified));
            }
        }

        return SurpriseInputs.Create(observations, lag, calendar);
    }

    private static string Key(ResultsFilingTime filing) => filing.PeriodEnd!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "|" + filing.BroadcastTs.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
