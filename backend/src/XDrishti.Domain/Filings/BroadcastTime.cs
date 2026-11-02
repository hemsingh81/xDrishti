namespace XDrishti.Domain.Filings;

/// <summary>The broadcast time of a filing as listed: a UTC instant (or null), the state and, when unavailable, the closed reason.</summary>
public sealed record BroadcastTime(DateTimeOffset? Instant, BroadcastState State, string? Reason)
{
    public static string Code(BroadcastState state) => state switch
    {
        BroadcastState.Ok => "ok",
        BroadcastState.Unavailable => "unavailable",
        BroadcastState.RowLevelTs => "row_level_ts",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
