namespace XDrishti.SimReader;

/// <summary>The recorded definition of an opening-range breakout the reader needs (spec P3-09c D4; docs 07, ADR 0037 A7, ADR 0038).</summary>
/// <param name="OrMinutes">The minutes of the opening range, counted from 09:15.</param>
/// <param name="RewardRisk">Target distance in multiples of the risk.</param>
/// <param name="FirstEntry">Earliest decision time (the close of the evaluation bar), inclusive.</param>
/// <param name="LastEntry">Latest decision time, inclusive.</param>
/// <param name="SquareOff">The square-off minute.</param>
/// <param name="EntryLimitPercent">The entry limit offset from the trigger close, in percent.</param>
/// <param name="Tick">The instrument's tick in rupees.</param>
/// <param name="EvaluationMinutes">The evaluation timeframe in minutes (15 for ORB v1).</param>
public sealed record ReaderStrategy(
    int OrMinutes,
    decimal RewardRisk,
    TimeOnly FirstEntry,
    TimeOnly LastEntry,
    TimeOnly SquareOff,
    decimal EntryLimitPercent,
    decimal Tick,
    int EvaluationMinutes = 15)
{
    /// <summary>ORB v1 as recorded: or_min 15, rr 2, 09:30 to 11:30, square-off 15:15, limit 0.1 %.</summary>
    public static ReaderStrategy OrbV1(decimal tick) =>
        new(15, 2m, new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), 0.1m, tick);
}
