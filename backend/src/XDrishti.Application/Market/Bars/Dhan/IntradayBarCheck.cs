using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Dhan;

/// <summary>What checking a source's bars left: the storable ones (oldest first, no minute twice), how many were repaired and how many were rejected, and an example of a rejection.</summary>
public sealed record CheckedIntradayBars(IReadOnlyList<Bar> Bars, long Repaired, long Rejected, string? RejectionExample);

/// <summary>
/// Checks bars a source returned before they are stored (ADR 0016 §7): every bar must start on an India minute inside a session (09:15–15:29, or an evening
/// special session) and obey the row rules of <see cref="BarRules"/> (repaired or rejected); a minute that comes twice (neighbouring windows share their boundary)
/// is kept once.
/// </summary>
public static class IntradayBarCheck
{
    public static CheckedIntradayBars Check(IReadOnlyList<Bar> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var byMinute = new Dictionary<long, Bar>(raw.Count);
        long repaired = 0, rejected = 0;
        string? example = null;
        foreach (var bar in raw)
        {
            if (!SessionClock.InAnySession(SessionClock.MinuteOfDay(bar.EpochSeconds)))
            {
                rejected++;
                example ??= $"{bar.Start:yyyy-MM-dd HH:mm} UTC is not inside a session (India time)";
                continue;
            }

            var verdict = BarRules.Check(bar, out var fixedBar);
            if (verdict is BarVerdict.Valid or BarVerdict.Repaired)
            {
                byMinute[fixedBar.EpochSeconds] = fixedBar; // a repeated minute: the later one wins (the windows overlap at their boundary and agree)
                if (verdict == BarVerdict.Repaired)
                {
                    repaired++;
                }
            }
            else
            {
                rejected++;
                example ??= $"{bar.Start:yyyy-MM-dd HH:mm} UTC: {verdict}";
            }
        }

        var bars = byMinute.Values.ToList();
        bars.Sort((a, b) => a.EpochSeconds.CompareTo(b.EpochSeconds));
        return new CheckedIntradayBars(bars, repaired, rejected, example);
    }
}
