using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>Criterion 7 of P4-06 (D11 oracle): the independent implementation agrees with <see cref="ExitSelector"/> on every field of the record over 5,000 seeded comparisons of random candidates, calendars, trades and options.</summary>
public sealed class SelectionOracleTests
{
    private const int Cases = 5000;

    private const double Tolerance = 1e-9;

    private static readonly double[] Lambdas = [0.0, 0.5, 1.0];

    private static SelectionInput Random(int seed)
    {
        var random = new System.Random(seed);
        var candidates = SelectionSeries.Line(random.Next(2, 5));
        if (random.Next(3) == 0)
        {
            candidates.Add(SelectionSeries.Policy("PYRAMID", ("add_at_rr", 1)));
        }

        if (random.Next(4) == 0)
        {
            candidates.AddRange([SelectionSeries.Policy("G", ("a", 1), ("b", 1)), SelectionSeries.Policy("G", ("a", 1), ("b", 2)), SelectionSeries.Policy("G", ("a", 2), ("b", 1)), SelectionSeries.Policy("G", ("a", 2), ("b", 2))]);
        }

        if (random.Next(5) == 0)
        {
            candidates = [.. candidates.OrderBy(_ => random.Next())];
        }

        var from = new DateOnly(2024, 4, 1).AddDays(random.Next(0, 40));
        var months = random.Next(6, 20);
        var holidays = new HashSet<DateOnly>(Enumerable.Range(0, 25).Select(_ => from.AddDays(random.Next(0, 600))));
        var sessions = SelectionSeries.Sessions(from, from.AddMonths(months).AddDays(30), holidays);
        var density = 0.15 + (random.NextDouble() * 0.85);
        var hold = random.Next(0, 6);
        var edges = candidates.Select(_ => (random.NextDouble() * 0.6) - 0.1).ToArray();
        var trades = new List<SelectionTrade>();
        for (var d = 0; d < sessions.Count && sessions[d] <= from.AddMonths(months); d++)
        {
            var common = SelectionSeries.Normal(random);
            for (var c = 0; c < candidates.Count; c++)
            {
                if (random.NextDouble() < density)
                {
                    trades.Add(new SelectionTrade(candidates[c].Id, sessions[d], sessions[Math.Min(d + random.Next(0, hold + 1), sessions.Count - 1)], edges[c] + (0.5 * common) + (0.9 * SelectionSeries.Normal(random))));
                }
            }
        }

        var options = new SelectionOptions(random.Next(2, 9), random.Next(1, 4), random.Next(0, 8), Lambdas[random.Next(3)], random.Next(3, 26), random.Next(5) != 0, random.Next(50, 81));
        return new SelectionInput("cell", "RFilled", false, new string('a', 64), candidates, trades, sessions, options);
    }

    private static void Near(double? actual, double? expected, string what)
    {
        (actual is null).ShouldBe(expected is null, what);
        if (actual is { } a)
        {
            a.ShouldBe(expected!.Value, Tolerance, what);
        }
    }

    [Fact]
    public void The_two_implementations_agree_on_every_field_over_five_thousand_seeded_comparisons()
    {
        var withWinners = 0;
        var stableCases = 0;
        var chanceCompared = 0;
        for (var seed = 1; seed <= Cases; seed++)
        {
            var input = Random(seed);

            var actual = ExitSelector.Select(input);
            var oracle = SelectionOracle.Run(input);

            var label = "seed " + seed;
            actual.Folds.Count.ShouldBe(oracle.Folds.Count, label);
            actual.DroppedWindows.ShouldBe(oracle.Dropped, label);
            for (var i = 0; i < oracle.Folds.Count; i++)
            {
                var f = actual.Folds[i];
                var g = oracle.Folds[i];
                var at = label + " fold " + (i + 1);
                (f.TestStart, f.TestEnd, f.TrainTrades, f.TestTrades, f.Embargoed, f.Purged).ShouldBe((g.Start, g.End, g.Train, g.Test, g.Embargoed, g.Purged), at);
                (f.Winner, f.OnPlateau, f.Isolated, f.LonePeak, f.Thin).ShouldBe((g.Winner, g.Plateau, g.Isolated, g.Lone, g.Thin), at);
                Near(f.WinnerTestScore, g.WinnerTestScore, at + " test score");
                f.Rows.Count.ShouldBe(g.Rows.Count, at);
                for (var r = 0; r < g.Rows.Count; r++)
                {
                    (f.Rows[r].Id, f.Rows[r].Qualified, f.Rows[r].TrainTrades, f.Rows[r].TestTrades).ShouldBe((g.Rows[r].Id, g.Rows[r].Qualified, g.Rows[r].TrainN, g.Rows[r].TestN), at);
                    Near(f.Rows[r].TrainScore, g.Rows[r].TrainScore, at + " train score");
                    Near(f.Rows[r].TestScore, g.Rows[r].TestScore, at + " test score");
                }

                withWinners += f.Winner is null ? 0 : 1;
            }

            (actual.FinalCandidate, actual.Chosen, actual.NoChoiceReason is not null).ShouldBe((oracle.Final, oracle.Chosen, oracle.NoChoice), label);
            (actual.Stability.Stable, actual.Stability.Modal, actual.Stability.Agreeing, actual.Stability.PositiveOutOfSample, actual.Stability.Folds, actual.Stability.LonePeaks).ShouldBe((oracle.Stable, oracle.Modal, oracle.Agreeing, oracle.Positive, oracle.Used, oracle.LonePeaks), label);
            if (oracle.Chance is { } chance)
            {
                Near(actual.Stability.ChancePass, chance, label + " chance");
                chanceCompared++;
            }

            actual.Pooled.Count.ShouldBe(oracle.Pooled.Count, label);
            for (var p = 0; p < oracle.Pooled.Count; p++)
            {
                (actual.Pooled[p].Name, actual.Pooled[p].Trades).ShouldBe((oracle.Pooled[p].Name, oracle.Pooled[p].N), label);
                Near(actual.Pooled[p].Score, oracle.Pooled[p].Score, label + " pooled");
            }

            stableCases += oracle.Stable == true ? 1 : 0;
        }

        withWinners.ShouldBeGreaterThan(2000, "the cases exercise real choices, not only empty folds");
        stableCases.ShouldBeGreaterThan(100, "and some stable verdicts");
        chanceCompared.ShouldBeGreaterThan(1000, "and the exact chance of passing by luck");
    }
}
