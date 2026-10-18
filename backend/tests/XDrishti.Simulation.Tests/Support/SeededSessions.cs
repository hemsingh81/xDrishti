using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The series of the look-ahead property of the replay (spec P3-04, criterion 14): ten consecutive weekdays, the first a plain F1-shaped day and each later one the F1 shape scaled so that it opens within 5 % of the previous close; about
/// one date in seven carries a planted jump (a flagged day: it opens at 1.5 or 0.6 times the previous close) and about one in seven is a 60-bar evening session (excluded: a short session). A tiny deterministic generator, no
/// <c>System.Random</c>.
/// </summary>
internal sealed class SeededSessions
{
    public const int Sessions = 10;

    private SeededSessions(Bar[] bars, DateOnly[] dates, int[] starts, int[] ends)
    {
        Bars = bars;
        Dates = dates;
        Starts = starts;
        Ends = ends;
    }

    public Bar[] Bars { get; }

    public DateOnly[] Dates { get; }

    /// <summary>Position in <see cref="Bars"/> of the first bar of each session.</summary>
    public int[] Starts { get; }

    /// <summary>Position after the last bar of each session.</summary>
    public int[] Ends { get; }

    public static SeededSessions Of(ulong seed)
    {
        var rng = new SeededSeries(seed);
        var bars = new List<Bar>();
        var dates = new DateOnly[Sessions];
        var starts = new int[Sessions];
        var ends = new int[Sessions];
        var date = ReplayDays.D1;
        var level = 105.0;
        for (var s = 0; s < Sessions; s++)
        {
            dates[s] = date;
            starts[s] = bars.Count;
            if (s == 0)
            {
                bars.AddRange(ReplayDays.F1(date));
            }
            else
            {
                var roll = rng.Int(7);
                if (roll == 0)
                {
                    bars.AddRange(ReplayDays.Minutes(date, "18:00", 60, level));
                }
                else
                {
                    var open = roll == 1 ? level * (rng.Int(2) == 0 ? 1.5 : 0.6) : level * (1 + ((rng.Unit() - 0.5) * 0.1));
                    var scaled = ReplayDays.Scaled(ReplayDays.F1(date), open / 99.0);
                    bars.AddRange(scaled);
                    level = scaled[^1].Close;
                }
            }

            ends[s] = bars.Count;
            date = date.DayOfWeek == DayOfWeek.Friday ? date.AddDays(3) : date.AddDays(1);
        }

        return new SeededSessions([.. bars], dates, starts, ends);
    }

    /// <summary>The bars up to and including session <paramref name="session"/> (0-based).</summary>
    public Bar[] Through(int session) => Bars[..Ends[session]];

    /// <summary>The bars with the sessions from <paramref name="first"/> to <paramref name="last"/> (0-based, both included) replaced by extreme bars: highs ×10, lows ÷10, zero volume and gaps of ±50 % in the open and the close; the epochs are unchanged.</summary>
    public Bar[] WithExtremes(int first, int last, ulong seed)
    {
        var rng = new SeededSeries(seed ^ 0x5EED);
        var copy = (Bar[])Bars.Clone();
        for (var i = Starts[first]; i < Ends[last]; i++)
        {
            var b = copy[i];
            copy[i] = new Bar(b.EpochSeconds, b.Open * (rng.Int(2) == 0 ? 1.5 : 0.5), b.High * 10, b.Low / 10, b.Close * (rng.Int(2) == 0 ? 1.5 : 0.5), 0);
        }

        return copy;
    }
}
