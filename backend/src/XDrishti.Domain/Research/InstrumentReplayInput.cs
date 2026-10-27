using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Research;

/// <summary>
/// Everything the replay of one instrument takes (spec P3-04 Scope 3, amended 2026-10-18 by P3-04a: it lives here so that the Application port can name it). <paramref name="RawBars"/> run from the instrument's first stored
/// bar to the end of <paramref name="To"/>, strictly ascending; a bar dated after <paramref name="To"/> is a caller error. The replay starts at the first stored session whatever <paramref name="From"/> is, and only the trades are cut to the window.
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Symbol">Its symbol, written on every trade.</param>
/// <param name="RawBars">The stored 1-minute bars, raw (not adjusted), ascending.</param>
/// <param name="Factors">The applied corporate actions; the replay adjusts a copy.</param>
/// <param name="CsvAdjustedThrough">The date the loaded CSV files reach, when the instrument has any; an action on or before it is marked <c>OnCsvBars</c>.</param>
/// <param name="Calendar">The trading calendar.</param>
/// <param name="Options">The thresholds of the data notes.</param>
/// <param name="From">First date of the window (India date, inclusive).</param>
/// <param name="To">Last date of the window (inclusive).</param>
/// <param name="Tick">The instrument's tick; absent only with a <paramref name="PresetSkip"/> (nothing is scanned then, so no fill needs it): for <see cref="SkipReason.TickSizeInvalid"/> because there is none, for <see cref="SkipReason.CsvAdjustedTwice"/> when the master's tick is refused as well. The record then has no tick.</param>
/// <param name="PresetSkip">A skip the runner decided (<see cref="SkipReason.CsvAdjustedTwice"/> or <see cref="SkipReason.TickSizeInvalid"/>): the replay classifies the sessions and hashes the bars and does not scan. An empty list gives <c>no_bars</c> whatever it says.</param>
/// <param name="SkipDetail">A note on the preset skip, kept in the record.</param>
/// <param name="ContextDays">The stored market-context days (P3-lift, ADR 0145), oldest first, shared by every instrument of a run; given only when the strategy reads the context, otherwise null. A decision reads the latest day strictly before its date.</param>
/// <param name="ContextNote">Why there are no <paramref name="ContextDays"/> for a strategy that reads the context (<c>no_context</c>); null otherwise.</param>
/// <param name="Costs">How the trades are sized and charged after the window cut (ADR 0042, D1 and D2); null for a gross replay. The handler of P3-04a passes none: a run through it is gross until P3-03f.</param>
public sealed record InstrumentReplayInput(
    Guid InstrumentId,
    string Symbol,
    IReadOnlyList<Bar> RawBars,
    AdjustmentFactors Factors,
    DateOnly? CsvAdjustedThrough,
    TradingCalendar Calendar,
    ReplayOptions Options,
    DateOnly From,
    DateOnly To,
    TickSpec? Tick,
    SkipReason? PresetSkip = null,
    string? SkipDetail = null,
    CostContext? Costs = null,
    IReadOnlyList<ContextDayRecord>? ContextDays = null,
    string? ContextNote = null);
