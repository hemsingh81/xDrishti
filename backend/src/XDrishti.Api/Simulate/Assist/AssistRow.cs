using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>A trade row of <c>pack.json</c> with the strategy definition the reader needs.</summary>
internal sealed record PackedTrade(int Seq, string TradeKey, string Symbol, ReaderSide Side, DateOnly Date, string Strategy, int OrMinutes, decimal RewardRisk);

/// <summary>What the machine read for one row before the engine's value was seen: <see cref="Unavailable"/> is the reason the second source had no day, otherwise <see cref="Reading"/> is the reader's trade (null: no trade found).</summary>
internal sealed record AssistRow(PackedTrade Pack, string? Unavailable, string Source, ReaderReading? Reading, decimal Tick, IReadOnlyList<MinuteComparison> Bars);

/// <summary>The verdict of the assistant (never the owner's) with the engine fields the reader differs on.</summary>
internal sealed record RowVerdict(AssistRow Row, string Verdict, IReadOnlyList<string> Fields);

internal static class Verdicts
{
    public const string AgreeAll = "agree_all";

    public const string BarsDiffer = "bars_differ";

    public const string ReaderDiffers = "reader_differs";

    public const string CannotRead = "cannot_read";

    public const string SourceUnavailable = "source_unavailable";

    public static IReadOnlyList<string> All { get; } = [AgreeAll, BarsDiffer, ReaderDiffers, CannotRead, SourceUnavailable];
}
