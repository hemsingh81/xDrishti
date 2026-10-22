using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>Builders of the synthetic comparisons the exit choice is tested on (P4-06, test plan): the selection takes trades, so no walker is needed. Seeded, so every series is the same on every machine.</summary>
internal static class SelectionSeries
{
    public static readonly DateOnly From = new(2024, 4, 1);

    public static readonly DateOnly To = new(2026, 4, 30);

    public static ExitCandidate Rr(double rr) => new("FIXED_RR", [new ExitParameterValue("rr", [rr])]);

    /// <summary>A line of <paramref name="count"/> FIXED_RR candidates, rr 1.5, 2, 2.5 ...</summary>
    public static List<ExitCandidate> Line(int count) => [.. Enumerable.Range(0, count).Select(i => Rr(1.5 + (0.5 * i)))];

    public static ExitCandidate Policy(string policy, params (string Name, double Value)[] parameters) =>
        new(policy, [.. parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new ExitParameterValue(p.Name, [p.Value]))]);

    /// <summary>Weekdays from <paramref name="from"/> to <paramref name="to"/> minus the holidays: a calendar for the embargo.</summary>
    public static List<DateOnly> Sessions(DateOnly from, DateOnly to, IReadOnlySet<DateOnly>? holidays = null)
    {
        var sessions = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && holidays?.Contains(d) != true)
            {
                sessions.Add(d);
            }
        }

        return sessions;
    }

    /// <summary>
    /// One trade a session for each candidate, on the same entries: <c>R = edge[c] + 0.6 * common + 0.8 * own</c> with standard normal draws, so the candidates share the market noise and differ by their edge and their own noise. An exit is
    /// <paramref name="holdSessions"/> sessions after the entry.
    /// </summary>
    public static SelectionInput Input(int seed, IReadOnlyList<ExitCandidate> candidates, double[] edge, SelectionOptions? options = null, int holdSessions = 0, string unit = "RFilled")
    {
        var random = new Random(seed);
        var sessions = Sessions(From, To.AddDays(21));
        var trades = new List<SelectionTrade>();
        for (var d = 0; d < sessions.Count; d++)
        {
            var entry = sessions[d];
            if (entry > To)
            {
                break;
            }

            var common = Normal(random);
            for (var c = 0; c < candidates.Count; c++)
            {
                trades.Add(new SelectionTrade(candidates[c].Id, entry, sessions[Math.Min(d + holdSessions, sessions.Count - 1)], edge[c] + (0.6 * common) + (0.8 * Normal(random))));
            }
        }

        return new SelectionInput("ORB 15m BUY intraday *", unit, false, new string('e', 64), candidates, trades, sessions, options ?? new SelectionOptions());
    }

    public static double Normal(Random random)
    {
        var u = 1.0 - random.NextDouble();
        var v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }
}
