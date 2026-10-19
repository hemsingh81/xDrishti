namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criterion 8 of P3-08 (D7), the non-vacuity of the comparison: each of the 16 categories, and each listed sub-case of them, is reached by at least 500 series, measured from <b>the oracle's own trace</b> (the rows of the rule
/// tables it applied), never from the engine's counters; once in the combined run and once from <c>OracleSeries</c> alone, so that the engine's generators cannot hide a gap in the oracle's. The floors apply to each listed item separately
/// (a stop-and-target minute with the open between and one with the open beyond the target are two floors, not one). The counts are printed; a shortfall fails the suite.
/// </summary>
public sealed class CoverageTests(ITestOutputHelper output)
{
    internal const int Floor = 500;

    /// <summary>The listed items of D7 and the trace row that says the oracle applied them.</summary>
    internal static readonly (string Item, string Row)[] SubCases =
    [
        ("1 a gap above the limit (E5)", OracleRows.E5Gap),
        ("2 a gap through the stop at entry (E6)", OracleRows.E6Gap),
        ("3 a stop gap exit", OracleRows.XStopGap),
        ("4 a target gap exit", OracleRows.XTargetGap),
        ("5 stop and target in one minute, open between", OracleRows.X4Between),
        ("6 stop and target in one minute, open beyond the target", OracleRows.X4Beyond),
        ("7 holes before the fill minute", OracleRows.E1Hole),
        ("8 holes inside the position", OracleRows.XHole),
        ("9a a hole at the square-off minute", OracleRows.X1Late),
        ("9b a hole at the due minute", OracleRows.X8Last),
        ("10a a price equal to the limit", OracleRows.TieLimit),
        ("10b a price equal to the stop", OracleRows.TieStop),
        ("10c a price equal to the target", OracleRows.TieTarget),
        ("10d a price on the grid", OracleRows.GridOn),
        ("10e a price off the grid", OracleRows.GridOff),
        ("11 zero-volume minutes", OracleRows.E0),
        ("12a a special (short) session", OracleRows.CalSpecial),
        ("12b a holiday between two sessions", OracleRows.CalHoliday),
        ("13a the end of the data inside an intraday position", OracleRows.X6),
        ("13b the end of the data inside a swing position", OracleRows.X9),
        ("13c the end of the data with an order pending", OracleRows.Pending),
        ("14a an overnight order Friday to Monday", OracleRows.E9Weekend),
        ("14b an overnight order over a holiday", OracleRows.E9Holiday),
        ("14c the 09:20 fill", OracleRows.E9Late),
        ("14d the 09:21 minute (no fill)", OracleRows.E9Past),
        ("14e the 09:15 minute with a widened range", OracleRows.E9Wide),
        ("15a MaxHold at the close of session N, the due minute present", OracleRows.X8Due),
        ("15b MaxHold at the close of session N, the due minute absent", OracleRows.X8Last),
        ("16a a candidate at 15:30 on the day a position leaves at the close", OracleRows.D11Close),
        ("16b a position ended by the data blocks", OracleRows.D11EndOfData),
    ];

    [Fact]
    public void Every_category_is_reached_by_at_least_500_series_of_the_authors_generator_alone()
    {
        var oracle = Corpus.OracleSeriesRun;
        var lines = new List<string> { $"OracleSeries alone: {oracle.Series} series, floor {Floor} per category (by the oracle's trace)" };
        var short_ = new List<string>();
        for (var category = 1; category <= OracleCategories.Count; category++)
        {
            lines.Add($"category {category,2} ({OracleCategories.Name(category)}): reached by {oracle.CategoryReached[category]} series of OracleSeries; generated for it: {oracle.CategoryGenerated[category]}");
            if (oracle.CategoryReached[category] < Floor)
            {
                short_.Add($"category {category}: {oracle.CategoryReached[category]}");
            }
        }

        Report.Write("coverage-categories.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        short_.ShouldBeEmpty();
    }

    [Fact]
    public void Every_listed_sub_case_is_reached_by_at_least_500_series_of_the_authors_generator_alone()
    {
        var oracle = Corpus.OracleSeriesRun;
        var lines = new List<string> { $"OracleSeries alone: {oracle.Series} series, floor {Floor} per listed item (by the oracle's trace)" };
        var short_ = new List<string>();
        foreach (var (item, row) in SubCases)
        {
            var count = oracle.RowSeries.GetValueOrDefault(row);
            lines.Add($"{item} [{row}]: {count}");
            if (count < Floor)
            {
                short_.Add($"{item}: {count}");
            }
        }

        Report.Write("coverage-sub-cases.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        short_.ShouldBeEmpty();
    }

    [Fact]
    public void In_the_combined_run_every_category_and_sub_case_reaches_its_floor_and_the_counts_are_printed()
    {
        var all = Corpus.All;
        var combinedSeries = all.Sum(t => t.Series);
        var lines = new List<string> { $"combined run: {combinedSeries} series ({string.Join(", ", all.Select(t => $"{t.Name} {t.Series}"))})" };
        var short_ = new List<string>();
        for (var category = 1; category <= OracleCategories.Count; category++)
        {
            var count = all.Sum(t => t.CategoryReached[category]);
            lines.Add($"category {category,2}: {count} series (OracleSeries {Corpus.OracleSeriesRun.CategoryReached[category]}, engine generators {count - Corpus.OracleSeriesRun.CategoryReached[category]})");
            if (count < Floor)
            {
                short_.Add($"category {category}: {count}");
            }
        }

        foreach (var (item, row) in SubCases)
        {
            var count = all.Sum(t => t.RowSeries.GetValueOrDefault(row));
            lines.Add($"{item} [{row}]: {count} (OracleSeries {Corpus.OracleSeriesRun.RowSeries.GetValueOrDefault(row)})");
            if (count < Floor)
            {
                short_.Add($"{item}: {count}");
            }
        }

        Report.Write("coverage-combined.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        short_.ShouldBeEmpty();
        combinedSeries.ShouldBeGreaterThanOrEqualTo(2000, "criterion 5: at least 2,000 series in total");
    }

    [Fact]
    public void The_gate_comparison_covers_at_least_500_multi_candidate_swing_series()
    {
        // criterion 6: gated series with several candidates, from the generators together; the 15:30-at-the-close and ended-by-the-data candidates are the sub-cases 16a and 16b above
        var gated = Corpus.All.Sum(t => t.GatedSeries) / 2;   // each series is compared in two fill modes

        output.WriteLine($"multi-candidate swing series (gate with several candidates): {gated}");
        gated.ShouldBeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public void The_sentinel_a_floor_that_is_not_reached_is_reported_as_short()
    {
        // the count of a category that the generator does not produce is far below the floor: the check above would fail on it
        var oracle = Corpus.OracleSeriesRun;

        oracle.RowSeries.GetValueOrDefault("no-such-row").ShouldBeLessThan(Floor);
        oracle.RowSeries.GetValueOrDefault(OracleRows.E9Wide).ShouldBeGreaterThanOrEqualTo(Floor);
    }
}
