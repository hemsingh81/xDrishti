using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The renderings that P3-03e freezes under sim-4 before it changes any rule (spec D8, criterion 8): the walker fixtures F and W, the hand rows H with and without slippage, 500 seeded intraday days, the swing rows M with fixture R and
/// the flagged boundary Q1, and the gate rows G with the window rows J and Q2 of P3-03d. The text is that of the sim-4 code; <see cref="View"/> is the only difference allowed under a later version (the engine label, the trade schema and the two counters
/// that P3-03e appends, when they are zero), so that "the same bytes in the sim-4 view" is a comparison of strings. The frozen files are <c>Golden/*.sim-4.frozen.txt</c> and are never regenerated.
/// </summary>
internal static class Sim4Rows
{
    private static readonly Regex NewCounters = new(@" (overnightEntries|orderPendingAtEnd)=0(?!\d)", RegexOptions.CultureInvariant);

    /// <summary>The text in the sim-4 view: the engine label of sim-4 and none of the counters appended since, which must be zero to disappear.</summary>
    public static string View(string text) => NewCounters.Replace(text.Replace("engine=" + SimulationEngine.Version, "engine=sim-4", StringComparison.Ordinal).Replace("schema=" + SimTrade.CurrentSchemaVersion, "schema=4", StringComparison.Ordinal), string.Empty);

    public static string Walker()
    {
        var text = new StringBuilder();
        var total = WalkCounters.Empty;
        foreach (var walkCase in Cases.All)
        {
            for (var i = 0; i < walkCase.Items.Count; i++)
            {
                var result = WalkAssert.Walk(walkCase, walkCase.Items[i]);
                text.Append(walkCase.Name).Append(" #").Append(i).Append(' ').Append(OutcomeFormatter.Render(result)).Append('\n');
                total = total.Add(result.Value.ToCounters());
            }
        }

        return View(text.Append("total ").Append(OutcomeFormatter.Render(total)).Append('\n').ToString());
    }

    public static string HRows()
    {
        var text = new StringBuilder();
        foreach (var hCase in HandCases.All)
        {
            foreach (var (label, fills) in new[] { ("modelled", FillRules.Default), ("exact", Cand.NoSlippage) })
            {
                text.Append(hCase.Hand.Name).Append(' ').Append(label).Append(' ').Append(OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(hCase.Hand.Walk(fills)))).Append('\n');
            }
        }

        return View(text.ToString());
    }

    public static string SeededBatches()
    {
        var lines = new StringBuilder();
        for (var first = 1; first <= SeededDays.Count; first += 50)
        {
            var text = new StringBuilder();
            foreach (var day in SeededDays.All.Where(d => d.Seed >= first && d.Seed < first + 50))
            {
                text.Append("seed ").Append(day.Seed).Append('\n');
                var counters = WalkCounters.Empty;
                foreach (var walk in day.Walks)
                {
                    var result = TradeWalker.Walk(walk.Candidate, day.Bars, walk.Rules);
                    text.Append(OutcomeFormatter.Render(result)).Append('\n');
                    counters = counters.Add(result.Value.ToCounters());
                }

                text.Append(OutcomeFormatter.Render(counters)).Append('\n');
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(View(text.ToString())))).ToLowerInvariant();
            lines.Append(CultureInfo.InvariantCulture, $"seeds {first}-{first + 49} {hash}\n");
        }

        return lines.ToString();
    }

    /// <summary>The rows M of the swing walker, fixture R through the simulator and the flagged boundary Q1: the text of <c>swing.golden.txt</c> without its first line (in the sim-4 view).</summary>
    public static string Swing() => View(SwingText());

    /// <summary>The same text as the swing golden file holds it (the live rendering, without the view).</summary>
    public static string SwingText()
    {
        var text = new StringBuilder();
        var total = WalkCounters.Empty;
        foreach (var swingCase in SwingCases.All)
        {
            var hand = swingCase.Hand;
            var result = TradeWalker.Walk(hand.Candidate(), hand.Bars, hand.Rules(FillRules.Default));
            text.Append(hand.Name).Append(' ').Append(OutcomeFormatter.Render(result)).Append('\n');
            total = total.Add(result.Value.ToCounters());
        }

        text.Append("total ").Append(OutcomeFormatter.Render(total)).Append('\n');

        var run = Sim.Run(TestStrategies.Swing1h(), FixtureR.Bars(Fri), Timeframe.H1, calendar: DefaultWeek());
        text.Append("fixture R\n").Append(Render.Result(run));

        var replay = Rep.Replay(Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default), FixtureR.CrashBars(Wed, "09:15 60.00/60.00/60.00/60.00"), Mon, Wed, calendar: DefaultWeek());
        text.Append("flagged boundary Q1\n").Append(RunText.Replay(replay));
        return text.ToString();
    }

    private static EntryCandidate At(IReadOnlyList<Bar> bars, DateOnly date, string decision, double stop, double triggerClose)
    {
        var own = Day.Epoch(date, Day.Minute(decision) - 1);
        var index = bars.ToList().FindIndex(b => b.EpochSeconds == own);
        return Cand.Make(Side.Buy, index, decision, stop, "09:45", (triggerClose, triggerClose, triggerClose, triggerClose), date: date);
    }

    private static string Gate(string name, IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, int sessions, double rewardRisk = 2.0)
    {
        var rules = new WalkRules(rewardRisk, WalkRules.NoSquareOff, FillRules.Default, Cand.Tick05, SwingHold.Create(sessions, null).Value);
        var outcomes = PositionGate.Apply(candidates, c => TradeWalker.Walk(c, bars, rules)).Value;
        var text = new StringBuilder();
        var total = WalkCounters.Empty;
        foreach (var outcome in outcomes)
        {
            text.Append(name).Append(' ').Append(OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(outcome.Walk))).Append('\n');
            total = total.Add(outcome.Walk.ToCounters());
        }

        return text.Append(name).Append(" total ").Append(OutcomeFormatter.Render(total)).Append('\n').ToString();
    }

    /// <summary>The gate rows G1, G2, G2b (long and short list) and G3, the window rows J1 to J4 and Q2 of P3-03d.</summary>
    public static string GateAndWindowRows()
    {
        var text = new StringBuilder();
        var g1 = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20), Day.Dense(Wed).Start(100.20).Set("10:15 99.40/99.50/98.90/99.00"));
        text.Append(Gate("G1", g1, [At(g1, Mon, "10:15", 99.00, 100.00), At(g1, Tue, "10:15", 99.00, 100.20), At(g1, Wed, "10:15", 98.00, 99.00), At(g1, Wed, "10:45", 98.00, 99.00)], 5));

        var g2 = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20));
        text.Append(Gate("G2", g2, [At(g2, Mon, "10:15", 99.00, 100.00), At(g2, Tue, "14:45", 99.00, 100.20), At(g2, Tue, "15:15", 99.00, 100.20)], 5));

        Day Wednesday()
        {
            var wed = Day.Dense(Wed).Start(100.20);
            for (var minute = Day.Minute("15:11"); minute <= Day.Minute("15:29"); minute++)
            {
                wed.SetEmpty(Day.Clock(minute) + " 100.20/100.20/100.20/100.20");
            }

            return wed;
        }

        var mon = Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20");
        var tue = Day.Dense(Tue).Start(100.20);
        var longRun = Day.Join(mon, tue, Wednesday(), Day.Dense(Thu).Start(100.20));
        var shortRun = Day.Join(mon, tue, Wednesday());
        text.Append(Gate("G2b-long", longRun, [At(longRun, Mon, "10:15", 99.00, 100.00), At(longRun, Wed, "15:15", 99.00, 100.20), At(longRun, Wed, "15:30", 99.00, 100.20), At(longRun, Thu, "10:15", 99.00, 100.20)], 3));
        text.Append(Gate("G2b-short", shortRun, [At(shortRun, Mon, "10:15", 99.00, 100.00), At(shortRun, Wed, "15:15", 99.00, 100.20), At(shortRun, Wed, "15:30", 99.00, 100.20)], 3));

        var g3 = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20).Set("10:15 103.00/103.20/102.90/103.10"));
        text.Append(Gate("G3", g3, [At(g3, Mon, "10:15", 99.00, 100.00), At(g3, Tue, "10:15", 99.00, 100.20)], 5, 10.0));

        var swing = Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default);
        foreach (var (name, bars, from, to) in new[]
        {
            ("J1", FixtureR.Bars(Wed), Mon, Wed), ("J2", FixtureR.Bars(Tue), Mon, Tue), ("J3", FixtureR.Bars(Fri), Tue, Fri), ("J4", FixtureR.Bars(Fri), Mon, Fri),
            ("Q2", FixtureR.CrashBars(Wed, "09:15 81.30/81.50/81.00/81.20"), Mon, Wed),
        })
        {
            text.Append(name).Append('\n').Append(RunText.Replay(Rep.Replay(swing, bars, from, to, calendar: DefaultWeek())));
        }

        return View(text.ToString());
    }
}
