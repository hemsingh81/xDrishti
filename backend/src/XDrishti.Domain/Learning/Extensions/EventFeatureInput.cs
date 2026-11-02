using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Everything <see cref="EventFeatures.Compute"/> may read for one decision (P15-05). The extractor hands over what it has; the function looks only at what is visible at <see cref="At"/>.</summary>
/// <param name="At">The decision instant.</param>
/// <param name="Lag">The lag between a broadcast or an intimation and the moment it counts as known.</param>
/// <param name="Calendar">The trading calendar.</param>
/// <param name="CalendarRows">The stored results meeting rows of the instrument.</param>
/// <param name="Filings">The broadcast times of the stored results filings of the instrument (state ok).</param>
/// <param name="StockCloses">The raw daily closes of the instrument by India date.</param>
/// <param name="IndexCloses">The NIFTY 50 daily closes by India date, or null when no market context is stored.</param>
/// <param name="Actions">The applied splits and bonuses of the instrument.</param>
public sealed record EventFeatureInput(
    DateTimeOffset At,
    TimeSpan Lag,
    TradingCalendar Calendar,
    IReadOnlyList<CalendarRow> CalendarRows,
    IReadOnlyList<ResultsFilingTime> Filings,
    IReadOnlyDictionary<DateOnly, double> StockCloses,
    IReadOnlyDictionary<DateOnly, double>? IndexCloses,
    IReadOnlyList<AppliedAction> Actions);
