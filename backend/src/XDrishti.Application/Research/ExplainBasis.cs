using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Research;

/// <summary>
/// What a run held that the trade cards need to recompute it (P3-04d): the prepared strategy, the calendar, the CSV-adjusted dates and, for a strategy that reads the market context, the stored context days it read (P3-lift) and, for one that names `not_event_day`, the results-calendar snapshot it read (P15-06) and the tier source for a strategy that names `min_turnover` or a conservative run (CF-05: without it a card replay would block every entry). It exists only in the process that made the run; a stored run has none, and its cards say so.
/// For a run with dated members (BK-11b) <c>Members</c> is the membership the run used, so the recomputation refuses the same entries.
/// </summary>
public sealed record ExplainBasis(IPreparedStrategy Strategy, TradingCalendar Calendar, IReadOnlyDictionary<Guid, DateOnly> CsvThrough, IReadOnlyList<ContextDayRecord>? ContextDays = null, EventCalendarSnapshot? Events = null, ResultsDayInputs? ResultsDays = null, IEntryTierSource? Tiers = null, bool Conservative = false, XDrishti.Domain.Research.DatedMembers? Members = null);
