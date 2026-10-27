using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Research;

/// <summary>
/// What a run held that the trade cards need to recompute it (P3-04d): the prepared strategy, the calendar, the CSV-adjusted dates and, for a strategy that reads the market context, the stored context days it read (P3-lift). It exists only in the process that made the run; a stored run has none, and its cards say so.
/// </summary>
public sealed record ExplainBasis(IPreparedStrategy Strategy, TradingCalendar Calendar, IReadOnlyDictionary<Guid, DateOnly> CsvThrough, IReadOnlyList<ContextDayRecord>? ContextDays = null);
