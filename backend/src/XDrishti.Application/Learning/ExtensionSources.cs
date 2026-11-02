using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Learning;

/// <summary>What an extraction reads besides the stores of one instrument, read once before the loop: the trading calendar, the lag, the results-calendar snapshot, the broadcast times of the stored results filings, and the NIFTY 50 closes (null when no market context is stored).</summary>
internal sealed record ExtensionSources(
    TradingCalendar Calendar,
    TimeSpan Lag,
    EventCalendarSnapshot Snapshot,
    IReadOnlyDictionary<Guid, IReadOnlyList<ResultsFilingTime>> Filings,
    IReadOnlyDictionary<DateOnly, double>? IndexCloses);
