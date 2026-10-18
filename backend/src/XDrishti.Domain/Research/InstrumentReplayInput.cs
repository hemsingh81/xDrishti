using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

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
/// <param name="Tick">The instrument's tick; absent only when <paramref name="PresetSkip"/> is <see cref="SkipReason.TickSizeInvalid"/>.</param>
/// <param name="PresetSkip">A skip the runner decided (<see cref="SkipReason.CsvAdjustedTwice"/> or <see cref="SkipReason.TickSizeInvalid"/>): the replay classifies the sessions and hashes the bars and does not scan. An empty list gives <c>no_bars</c> whatever it says.</param>
/// <param name="SkipDetail">A note on the preset skip, kept in the record.</param>
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
    string? SkipDetail = null);
