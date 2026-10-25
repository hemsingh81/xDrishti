using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Research;

/// <summary>
/// What a run held that the trade cards need to recompute it (P3-04d): the prepared strategy, the calendar and the CSV-adjusted dates. It exists only in the process that made the run; a stored run has none, and its cards say so.
/// </summary>
public sealed record ExplainBasis(IPreparedStrategy Strategy, TradingCalendar Calendar, IReadOnlyDictionary<Guid, DateOnly> CsvThrough);
