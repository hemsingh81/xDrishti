using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>A trade row of <c>pack.json</c> with the strategy definition the reader needs.</summary>
internal sealed record PackedTrade(int Seq, string TradeKey, string Symbol, ReaderSide Side, DateOnly Date, string Strategy, int OrMinutes, decimal RewardRisk);

/// <summary>
/// What the machine read for one row before the engine's value was seen: <see cref="Unavailable"/> is the reason the second source had no day, otherwise <see cref="Reading"/> is the reader's trade on the SECOND-source bars (null: no trade found)
/// and <see cref="StoredReading"/> is its trade on the STORED bars, the data the engine used (null: none found, <see cref="StoredNote"/> says why).
/// </summary>
internal sealed record AssistRow(PackedTrade Pack, string? Unavailable, string Source, ReaderReading? Reading, decimal Tick, IReadOnlyList<MinuteComparison> Bars, bool TickFallback = false, string? OffsetNote = null, ReaderReading? StoredReading = null, string? StoredNote = null, string? SecondNote = null);

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
