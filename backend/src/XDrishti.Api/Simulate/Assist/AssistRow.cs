using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>
/// A trade row of <c>pack.json</c> with the strategy definition the reader needs. <see cref="Date"/> is the ENTRY (fill) date, <see cref="DecisionDate"/> the date of the decision, the last segment of the trade key (ADR 0043 D6);
/// they differ for a swing trade whose order rests overnight (BASE_BREAKOUT) and are equal for an intraday trade.
/// </summary>
internal sealed record PackedTrade(int Seq, string TradeKey, string Symbol, ReaderSide Side, DateOnly Date, string Strategy, int OrMinutes, decimal RewardRisk, IReadOnlyDictionary<string, decimal>? Parameters = null, DateOnly? DecisionDate = null)
{
    /// <summary>The date of the decision.</summary>
    public DateOnly Signal => DecisionDate ?? Date;

    /// <summary>The dates the second source is asked for: the decision day and the entry day of a swing trade (the two days whose bars decide the entry), the day itself otherwise.</summary>
    public IReadOnlyList<DateOnly> FetchDates => string.Equals(Strategy, "BASE_BREAKOUT", StringComparison.Ordinal) && Signal != Date ? [Signal, Date] : [Date];

    /// <summary>A parameter the pack records. Callers check <see cref="Missing"/> first.</summary>
    public decimal Parameter(string name) => Parameters![name];

    /// <summary>The parameters a reader needs that the pack does not name (a misspelled or absent one is a refusal, not a default).</summary>
    public IReadOnlyList<string> Missing(IReadOnlyList<string> required) => [.. required.Where(n => Parameters is null || !Parameters.ContainsKey(n))];
}

/// <summary>
/// What the machine read for one row before the engine's value was seen: <see cref="Unavailable"/> is the reason the second source had no day, otherwise <see cref="Reading"/> is the reader's trade on the SECOND-source bars (null: no trade found)
/// and <see cref="StoredReading"/> is its trade on the STORED bars, the data the engine used (null: none found, <see cref="StoredNote"/> says why). <see cref="HistoryCaveat"/> says why the stored history the multi-session readers used may differ from the engine's (a corporate action or the seam inside it): a data finding, never a defect.
/// </summary>
internal sealed record AssistRow(PackedTrade Pack, string? Unavailable, string Source, ReaderReading? Reading, decimal Tick, IReadOnlyList<MinuteComparison> Bars, bool TickFallback = false, string? OffsetNote = null, ReaderReading? StoredReading = null, string? StoredNote = null, string? SecondNote = null, string? HistoryCaveat = null);

/// <summary>The verdict of the assistant (never the owner's). <c>Fields</c> are the engine fields on which the reader on the STORED bars differs (rules and implementation); <c>SecondFields</c> those on which the reader on the second source differs (data).</summary>
internal sealed record RowVerdict(AssistRow Row, string Verdict, IReadOnlyList<string> Fields, IReadOnlyList<string> SecondFields)
{
    public RowVerdict(AssistRow row, string verdict, IReadOnlyList<string> fields)
        : this(row, verdict, fields, [])
    {
    }
}

/// <summary>The independent counters over all rows, whatever their verdict (so that precedence masks nothing); one place for the console and the report.</summary>
internal static class AssistCounters
{
    public static IReadOnlyList<(string Label, int Count)> All(IReadOnlyList<RowVerdict> verdicts) =>
    [
        ("reader on the stored bars differs from the engine (rows, whatever their verdict)", verdicts.Count(v => v.Fields.Count > 0)),
        ("reader on the second source differs from the engine (rows, whatever their verdict)", verdicts.Count(v => v.SecondFields.Count > 0)),
        ("rows with a gross bar problem (rows, whatever their verdict)", verdicts.Count(v => v.Row.Bars.Any(b => b.Gross))),
    ];

    public static IEnumerable<string> Lines(IReadOnlyList<RowVerdict> verdicts) => All(verdicts).Select(c => c.Label + ": " + c.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

internal static class Verdicts
{
    public const string AgreeAll = "agree_all";

    public const string BarsDiffer = "bars_differ";

    public const string ReaderDiffers = "reader_differs";

    public const string DataSensitive = "data_sensitive";

    public const string CannotRead = "cannot_read";

    public const string SourceUnavailable = "source_unavailable";

    public static IReadOnlyList<string> All { get; } = [AgreeAll, DataSensitive, BarsDiffer, ReaderDiffers, CannotRead, SourceUnavailable];
}
