using System.Globalization;

namespace XDrishti.Domain.Market.Actions;

/// <summary>
/// Finds suspected splits and bonus issues in a daily series (ADR 0017 §5). Pure and deterministic: the same bars always give the same candidates, so running it again suggests nothing new.
/// A day <c>d</c> is a candidate when
/// <list type="number">
/// <item>its open, or else its close, is within <see cref="Tolerance"/> (2.5 %) of a common price factor of the previous day's close: 1/2, 1/3, 1/4, 1/5, 1/10, 1/20, 2/3 or 3/4;</item>
/// <item>its volume is at least <see cref="VolumeMultiple"/> (1.5) times the median volume of the 20 sessions before it (at least <see cref="MinHistory"/> sessions must exist and the volume must be above 0);</item>
/// <item>the move is not reversed: neither the close of <c>d</c> nor the closes of the next 3 sessions that exist come back above the geometric middle between the previous close and the factor
/// (<c>previousClose × √factor</c>). A candidate on the newest days is judged on the sessions there are.</item>
/// </list>
/// The ratio found is mapped to ONE reading, the more common one, and the note names the other: 1/2 .. 1/20 are suggested as splits <c>1:2 .. 1:20</c> (the same drop is a bonus <c>1:1 .. 19:1</c>);
/// 2/3 is a bonus 1:2 (also read as split 2:3) and 3/4 a bonus 1:3 (split 3:4). Reverse splits (a price rise) are not looked for.
/// </summary>
public static class ActionDetector
{
    public const double Tolerance = 0.025;

    public const double VolumeMultiple = 1.5;

    public const int VolumeSessions = 20;

    public const int MinHistory = 10;

    public const int ReversalSessions = 3;

    private sealed record Reading(double Factor, CorporateActionKind Kind, int Numerator, int Denominator, string Alternative);

    private static readonly Reading[] Readings =
    [
        new(1.0 / 2, CorporateActionKind.Split, 1, 2, "bonus 1:1"),
        new(1.0 / 3, CorporateActionKind.Split, 1, 3, "bonus 2:1"),
        new(1.0 / 4, CorporateActionKind.Split, 1, 4, "bonus 3:1"),
        new(1.0 / 5, CorporateActionKind.Split, 1, 5, "bonus 4:1"),
        new(1.0 / 10, CorporateActionKind.Split, 1, 10, "bonus 9:1"),
        new(1.0 / 20, CorporateActionKind.Split, 1, 20, "bonus 19:1"),
        new(2.0 / 3, CorporateActionKind.Bonus, 1, 2, "split 2:3"),
        new(3.0 / 4, CorporateActionKind.Bonus, 1, 3, "split 3:4"),
    ];

    /// <summary>The candidates on days from <paramref name="from"/> on (the earlier days only give context), by date ascending. <paramref name="bars"/> are sorted by date, ascending.</summary>
    public static IReadOnlyList<ActionCandidate> Detect(IReadOnlyList<DailyBar> bars, DateOnly from)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var found = new List<ActionCandidate>();
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i].Date < from)
            {
                continue;
            }

            if (Examine(bars, i) is { } candidate)
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    private static ActionCandidate? Examine(IReadOnlyList<DailyBar> bars, int i)
    {
        var today = bars[i];
        var previous = bars[i - 1].Close;
        if (previous <= 0 || today.Open <= 0 || today.Close <= 0 || today.Volume <= 0)
        {
            return null;
        }

        var ratioOpen = today.Open / previous;
        var ratioClose = today.Close / previous;
        var basis = "Open";
        var ratio = ratioOpen;
        var reading = Match(ratioOpen);
        if (reading is null)
        {
            basis = "Close";
            ratio = ratioClose;
            reading = Match(ratioClose);
        }

        if (reading is null)
        {
            return null;
        }

        var median = MedianVolume(bars, i);
        if (median is not { } usual || today.Volume < VolumeMultiple * usual)
        {
            return null;
        }

        if (Reversed(bars, i, previous, reading.Factor))
        {
            return null;
        }

        var multiple = usual > 0 ? today.Volume / usual : double.PositiveInfinity;
        var seen = string.Create(CultureInfo.InvariantCulture, $"{basis} was {ratio:0.000} of the previous close");
        var volume = double.IsInfinity(multiple) ? "on far more than the usual volume" : string.Create(CultureInfo.InvariantCulture, $"on {multiple:0.0}x the usual volume");
        var note = $"{seen} {volume}. Could also be {reading.Alternative}.";
        return new ActionCandidate(today.Date, reading.Kind, reading.Numerator, reading.Denominator, note);
    }

    private static Reading? Match(double ratio)
    {
        Reading? best = null;
        var bestDistance = double.MaxValue;
        foreach (var reading in Readings)
        {
            var distance = Math.Abs(ratio / reading.Factor - 1.0);
            if (distance <= Tolerance && distance < bestDistance)
            {
                best = reading;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static double? MedianVolume(IReadOnlyList<DailyBar> bars, int i)
    {
        var start = Math.Max(0, i - VolumeSessions);
        var count = i - start;
        if (count < MinHistory)
        {
            return null;
        }

        var volumes = new double[count];
        for (var k = 0; k < count; k++)
        {
            volumes[k] = bars[start + k].Volume;
        }

        Array.Sort(volumes);
        return count % 2 == 1 ? volumes[count / 2] : (volumes[count / 2 - 1] + volumes[count / 2]) / 2.0;
    }

    private static bool Reversed(IReadOnlyList<DailyBar> bars, int i, double previousClose, double factor)
    {
        var middle = previousClose * Math.Sqrt(factor);
        var last = Math.Min(bars.Count - 1, i + ReversalSessions);
        for (var j = i; j <= last; j++)
        {
            if (bars[j].Close > middle)
            {
                return true;
            }
        }

        return false;
    }
}
