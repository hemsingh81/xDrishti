using System.Globalization;
using XDrishti.Application.Abstractions.Market.Eod;

namespace XDrishti.Application.Market.Eod;

/// <summary>One difference between the official daily row and the minute data of the same session: <c>volume</c> (percent apart beyond the tolerance) or <c>high</c> (the daily high is below the highest minute high by more than one tick).</summary>
public sealed record DailyMinuteFinding(string Code, string Symbol, DateOnly TradeDate, string Field, string Message);

/// <summary>
/// The daily-versus-minute rule (CF-04 AC 15). A finding, never a gate. Volume: the minute sum must be within the tolerance of the daily traded quantity. High: the daily high must not be below the highest minute high by more than one tick (the envelope).
/// The close is NOT compared (the official close is not the last minute's close). Pure.
/// </summary>
public static class DailyMinuteComparer
{
    public const string Code = "daily_minute_mismatch";

    public static IReadOnlyList<DailyMinuteFinding> Compare(IEnumerable<DailyMinuteRow> rows, double tolerancePct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var findings = new List<DailyMinuteFinding>();
        foreach (var row in rows)
        {
            if (row.DailyQty > 0)
            {
                var diff = Math.Abs(row.DailyQty - row.MinuteVolume) * 100.0 / row.DailyQty;
                if (diff > tolerancePct)
                {
                    findings.Add(new DailyMinuteFinding(Code, row.Symbol, row.TradeDate, "volume", string.Create(CultureInfo.InvariantCulture, $"minute volume {row.MinuteVolume} is {diff:0.00} % away from the daily traded quantity {row.DailyQty} (tolerance {tolerancePct} %)")));
                }
            }

            if (row.DailyHigh + row.TickSize < row.MinuteHigh)
            {
                findings.Add(new DailyMinuteFinding(Code, row.Symbol, row.TradeDate, "high", string.Create(CultureInfo.InvariantCulture, $"daily high {row.DailyHigh} is below the highest minute high {row.MinuteHigh} by more than one tick ({row.TickSize})")));
            }
        }

        return findings;
    }
}
