namespace XDrishti.Execution.Domain.Limits;

/// <summary>What the mark-to-market check of the day came to: below the cap, at or beyond it (halt), or not computable (no halt, entries refused).</summary>
public enum LossHaltVerdict
{
    Clear,
    Halt,
    Unavailable,
}

/// <summary>
/// The day's loss cap of spec P12-09 (D1, criteria 3 and 4). Two rules over the same number <c>max_loss_per_day</c>: an entry is refused when it could take the day's worst case past the cap (stops assumed to fill at their price; a profit
/// gives no room), and the day halts when the mark-to-market itself reaches the cap. An input that cannot be read never passes. Pure.
/// </summary>
public static class LossCap
{
    /// <summary>
    /// The entry rule: refused when <c>max(0, -realised) + openRisk + newRisk &gt; cap</c>; the boundary is allowed. Null when the entry fits, else the code (<see cref="AutoLiveCodes.LossDay"/>, or
    /// <see cref="AutoLiveCodes.LossUnavailable"/> when the realised result or the open risk to the stops cannot be read).
    /// </summary>
    public static string? Check(decimal? realisedToday, decimal? openRiskToStops, decimal newRisk, decimal cap)
    {
        if (realisedToday is not { } realised || openRiskToStops is not { } open || open < 0m || newRisk < 0m)
        {
            return AutoLiveCodes.LossUnavailable;
        }

        return WorstCase(realised, open, newRisk) > cap ? AutoLiveCodes.LossDay : null;
    }

    /// <summary>The day's worst case: the realised loss (a profit counts as zero), the risk of the open positions to their stops and the new entry's risk to its stop.</summary>
    public static decimal WorstCase(decimal realisedToday, decimal openRiskToStops, decimal newRisk) => Math.Max(0m, -realisedToday) + openRiskToStops + newRisk;

    /// <summary>The halt rule: realised plus unrealised at or below minus the cap halts. An unreadable mark, or no cap (an incomplete sheet), is <see cref="LossHaltVerdict.Unavailable"/>: it does not halt, it refuses new entries.</summary>
    public static LossHaltVerdict Halt(decimal? realisedToday, decimal? unrealised, decimal? cap) =>
        realisedToday is not { } r || unrealised is not { } u || cap is not { } c || c <= 0m ? LossHaltVerdict.Unavailable
        : r + u <= -c ? LossHaltVerdict.Halt
        : LossHaltVerdict.Clear;
}
