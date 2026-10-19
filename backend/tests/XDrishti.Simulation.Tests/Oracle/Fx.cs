
namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>One listed minute in the notation of the specs: <c>HH:MM O/H/L/C</c>, volume 1,000 unless stated.</summary>
internal readonly record struct Mn(string Time, decimal O, decimal H, decimal L, decimal C, long Vol);

/// <summary>The fixture notation of the specs as a tiny builder.</summary>
internal static class Fx
{
    internal const string Mon = "2026-03-02";
    internal const string Tue = "2026-03-03";
    internal const string Wed = "2026-03-04";
    internal const string Thu = "2026-03-05";
    internal const string Fri = "2026-03-06";
    internal const string Sat = "2026-03-07";
    internal const string Mon2 = "2026-03-09";

    internal static Mn M(string time, decimal o, decimal h, decimal l, decimal c) => new(time, o, h, l, c, 1000);

    internal static Mn V0(string time, decimal o, decimal h, decimal l, decimal c) => new(time, o, h, l, c, 0);

    internal static int Mi(string hhmm) => (int.Parse(hhmm[..2], System.Globalization.CultureInfo.InvariantCulture) * 60)
        + int.Parse(hhmm[3..], System.Globalization.CultureInfo.InvariantCulture);

    internal static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    internal static long E(string date, string hhmm) => OracleClock.Epoch(D(date), Mi(hhmm));

    internal static OracleCandidate Cand(Tape tape, OracleSide side, string date, string decision, decimal trig, decimal stop) =>
        new(side, tape.IndexOf(E(date, decision) - 60), E(date, decision), (double)trig, (double)stop);

    internal static OracleRules Intraday(OracleSlippage? slip = null, decimal tick = 0.05m, double? rr = 2.0) =>
        new(rr, 915, 0.1, slip ?? OracleSlippage.Default, tick);

    internal static OracleRules Swing(int n, OracleCalendar? cal = null, OracleSlippage? slip = null, decimal tick = 0.05m) =>
        new(2.0, 0, 0.1, slip ?? OracleSlippage.Default, tick, new OracleHold(n, cal));

    internal static OracleCalendar Cal(string[] holidays, params (string Date, string From, string To)[] special) =>
        new(holidays.Select(D), special.ToDictionary(s => D(s.Date), s => new OracleSession(Mi(s.From), Mi(s.To))));
}

/// <summary>A list of minutes built day by day: a minute that is not listed repeats the previous close.</summary>
internal sealed class Tape
{
    private readonly List<OracleBar> _bars = [];
    private decimal? _prev;

    internal Tape Day(string date, string from, string to, decimal? start = null, IEnumerable<Mn>? listed = null, params string[] absent)
    {
        DateOnly d = Fx.D(date);
        var map = new Dictionary<int, Mn>();
        foreach (Mn m in listed ?? [])
        {
            int mi = Fx.Mi(m.Time);
            if (mi < Fx.Mi(from) || mi > Fx.Mi(to))
            {
                throw new InvalidOperationException("listed minute outside the day: " + m.Time);
            }

            map[mi] = m;
        }

        var gone = new HashSet<int>();
        foreach (string a in absent)
        {
            int dash = a.IndexOf('-', StringComparison.Ordinal);
            int lo = Fx.Mi(dash < 0 ? a : a[..dash]);
            int hi = Fx.Mi(dash < 0 ? a : a[(dash + 1)..]);
            for (int x = lo; x <= hi; x++)
            {
                gone.Add(x);
            }
        }

        decimal prev = _prev ?? start ?? throw new InvalidOperationException("no start price");
        for (int m = Fx.Mi(from); m <= Fx.Mi(to); m++)
        {
            if (gone.Contains(m))
            {
                continue;
            }

            long epoch = OracleClock.Epoch(d, m);
            if (map.TryGetValue(m, out Mn mn))
            {
                _bars.Add(new OracleBar(epoch, (double)mn.O, (double)mn.H, (double)mn.L, (double)mn.C, mn.Vol));
                prev = mn.C;
            }
            else
            {
                _bars.Add(new OracleBar(epoch, (double)prev, (double)prev, (double)prev, (double)prev, 1000));
            }
        }

        _prev = prev;
        return this;
    }

    internal int IndexOf(long epoch)
    {
        int i = _bars.FindIndex(b => b.EpochSeconds == epoch);
        return i >= 0 ? i : throw new InvalidOperationException("no minute at " + epoch);
    }

    internal OracleBar[] ToArray() => [.. _bars];
}
