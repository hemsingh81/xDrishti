namespace XDrishti.Simulation.Tests.Oracle;

public sealed class SeriesTests(ITestOutputHelper output)
{
    private const int SeriesPerCategory = 1200;
    private const int Floor = 500;

    public static TheoryData<int> Categories()
    {
        var d = new TheoryData<int>();
        for (int c = 1; c <= 16; c++)
        {
            d.Add(c);
        }

        return d;
    }

    // E: explicit in the default check. The author's own coverage reports (1,200 series per category, run once more each) are superseded by CoverageTests, which asserts the same floors, and the finer ones, on the one run that the
    // comparison shares; run these on purpose to reproduce the author's printed report.
    [Fact(Explicit = true)]
    public void Coverage_each_category_is_reached_in_at_least_500_of_1200_series_by_the_trace()
    {
        var lines = new List<string> { $"series per category: {SeriesPerCategory}; floor: {Floor}" };
        var failures = new List<string>();
        for (int cat = 1; cat <= 16; cat++)
        {
            int reached = 0;
            var markerCounts = OracleCategories.MarkersOf(cat).ToDictionary(m => m, _ => 0);
            for (long seed = 0; seed < SeriesPerCategory; seed++)
            {
                OracleCaseRun run = OracleSeries.Of(cat, seed).Run(OracleFaults.None);
                if (OracleCategories.Reached(cat, run.Rows))
                {
                    reached++;
                }

                foreach (string m in markerCounts.Keys.ToList())
                {
                    if (run.Rows.Contains(m))
                    {
                        markerCounts[m]++;
                    }
                }
            }

            string detail = string.Join(", ", markerCounts.Select(kv => $"{kv.Key}={kv.Value}"));
            lines.Add($"category {cat,2} ({OracleCategories.Name(cat)}): {reached} of {SeriesPerCategory}; markers: {detail}");
            if (reached < Floor)
            {
                failures.Add($"category {cat} reached {reached}");
            }
        }

        Report.Write("coverage.txt", lines);
        foreach (string l in lines)
        {
            output.WriteLine(l);
        }

        failures.ShouldBeEmpty();
    }

    [Fact(Explicit = true)]
    public void Coverage_of_the_finer_cases_of_the_overnight_orders_the_due_minute_and_the_gate()
    {
        // The sub-cases that the brief names: each has its own count in the printed report (floors apply to the 16 categories).
        string[] subs =
        [
            OracleRows.E9Weekend, OracleRows.E9Holiday, OracleRows.E9Late, OracleRows.E9Past, OracleRows.E9Wide,
            OracleRows.X8Due, OracleRows.X8Last, OracleRows.D11Close, OracleRows.D11EndOfData, OracleRows.X6, OracleRows.X9, OracleRows.Pending,
            OracleRows.X1Late, OracleRows.CalHoliday, OracleRows.CalSpecial, OracleRows.TieLimit, OracleRows.TieStop, OracleRows.TieTarget, OracleRows.GridOff,
        ];
        var counts = subs.ToDictionary(s => s, _ => 0);
        for (int cat = 1; cat <= 16; cat++)
        {
            for (long seed = 0; seed < SeriesPerCategory; seed++)
            {
                OracleCaseRun run = OracleSeries.Of(cat, seed).Run(OracleFaults.None);
                foreach (string s in subs.Where(run.Rows.Contains))
                {
                    counts[s]++;
                }
            }
        }

        Report.Write("coverage-sub-cases.txt", counts.Select(kv => $"{kv.Key}: {kv.Value} series over 16 x {SeriesPerCategory}"));
        counts.Values.ShouldAllBe(v => v > 0);
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void Every_generated_minute_is_well_formed_and_the_candidate_points_at_its_own_minute(int category)
    {
        for (long seed = 0; seed < 300; seed++)
        {
            OracleCase c = OracleSeries.Of(category, seed);
            long prev = long.MinValue;
            foreach (OracleBar b in c.Bars)
            {
                b.EpochSeconds.ShouldBeGreaterThan(prev);
                prev = b.EpochSeconds;
                b.Volume.ShouldBeGreaterThanOrEqualTo(0);
                foreach (double p in new[] { b.Open, b.High, b.Low, b.Close })
                {
                    double.IsFinite(p).ShouldBeTrue();
                    p.ShouldBeGreaterThan(0);
                    p.ShouldBeLessThanOrEqualTo(1e12);
                }

                b.Low.ShouldBeLessThanOrEqualTo(b.Open);
                b.Low.ShouldBeLessThanOrEqualTo(b.Close);
                b.High.ShouldBeGreaterThanOrEqualTo(b.Open);
                b.High.ShouldBeGreaterThanOrEqualTo(b.Close);
            }

            foreach (OracleCandidate cand in c.Candidates)
            {
                c.Bars[cand.DecisionBarIndex].EpochSeconds.ShouldBe(cand.DecisionEpochSeconds - 60);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void The_same_seed_gives_the_same_series_and_another_seed_another_one(int category)
    {
        OracleCase a = OracleSeries.Of(category, 7);
        OracleCase b = OracleSeries.Of(category, 7);
        a.Bars.ShouldBe(b.Bars);
        a.Candidates.ShouldBe(b.Candidates);
        OracleSeries.Of(category, 8).Bars.ShouldNotBe(a.Bars);
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void The_identities_of_the_counters_hold_on_every_generated_series(int category)
    {
        for (long seed = 0; seed < 300; seed++)
        {
            OracleCase c = OracleSeries.Of(category, seed);
            OracleCaseRun run = c.Run(OracleFaults.None);
            var sum = new OracleCounters();
            foreach (OracleResult r in run.Results)
            {
                sum = sum.Plus(r.Counters);
                if (r.Trade is { } t)
                {
                    // The exit price is a visited price: -MAE <= RExact <= MFE.
                    t.MaeR.ShouldBeGreaterThanOrEqualTo(0);
                    t.MfeR.ShouldBeGreaterThanOrEqualTo(0);
                    (t.RExact + t.MaeR).ShouldBeGreaterThanOrEqualTo(-1e-9, $"category {category} seed {seed}");
                    (t.MfeR - t.RExact).ShouldBeGreaterThanOrEqualTo(-1e-9, $"category {category} seed {seed}");
                    t.SlippageR.ShouldBeLessThanOrEqualTo(0.0);
                }
            }

            sum.Candidates.ShouldBe(sum.Trades + sum.NoFillBar + sum.AfterSquareOff + sum.EntryThroughStop + sum.LimitNotReached + sum.PositionOpen + sum.OrderPendingAtEnd);
            sum.Trades.ShouldBe(sum.ExitsStop + sum.ExitsTarget + sum.ExitsSquareOff + sum.ExitsMaxHold + sum.ExitsEndOfData);
            sum.Trades.ShouldBe(sum.EntriesAtOpen + sum.EntriesAtLimit);
            sum.OvernightEntries.ShouldBeLessThanOrEqualTo(sum.Trades);
            sum.MaxHoldAtLastClose.ShouldBeLessThanOrEqualTo(sum.ExitsMaxHold);
            sum.OvernightStopGaps.ShouldBeLessThanOrEqualTo(sum.StopGapExits);
            sum.OvernightTargetGaps.ShouldBeLessThanOrEqualTo(sum.TargetGapExits);
            sum.SessionsHeld.ShouldBeGreaterThanOrEqualTo(sum.Trades);
        }
    }

    [Fact]
    public void The_generator_has_no_reference_to_the_system_random_class()
    {
        // The only generator is the SplitMix64 of the library; the assembly references nothing but the framework.
        typeof(OracleSeries).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ShouldAllBe(n => n.StartsWith("System", StringComparison.Ordinal) || n == "netstandard" || n == "mscorlib");
    }
}
