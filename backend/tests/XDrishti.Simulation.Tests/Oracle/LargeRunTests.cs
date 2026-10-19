namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The explicit larger counts of P3-08 (acceptance criterion 12: "counts of 5,000 per category are explicit tests and stay out of <c>scripts/check.sh</c>"): O's generator at 5,000 series in each of the 16 categories, both fill modes, the
/// oracle against the engine on every field. Run on purpose, for example before P3-09 or after a change of a rule:
/// <c>dotnet test --project tests/XDrishti.Simulation.Tests --filter-class XDrishti.Simulation.Tests.Oracle.LargeRunTests --explicit on</c>.
/// </summary>
public sealed class LargeRunTests(ITestOutputHelper output)
{
    private const int PerCategory = 5_000;

    [Fact(Explicit = true)]
    public void Five_thousand_series_per_category_agree_with_the_engine_and_reach_every_floor_five_times_over()
    {
        var stats = Corpus.Stats;
        var tally = Corpus.RunOracleSeries(_ => PerCategory);
        var lines = new List<string>
        {
            $"OracleSeries at {PerCategory} per category: series {tally.Series}, comparisons {tally.Comparisons}, candidates {tally.Candidates}, trades {tally.Trades}, blocked {tally.Blocked}, disagreements {tally.DisagreementCount}",
            $"ratio fields so far in this process: {stats.Compared} compared, {stats.NotExact} not bit-equal, largest difference {stats.MaxDifference:R}",
        };
        foreach (var (item, row) in CoverageTests.SubCases)
        {
            lines.Add($"{item} [{row}]: {tally.RowSeries.GetValueOrDefault(row)}");
        }

        Report.Write("large-run.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        tally.DisagreementCount.ShouldBe(0, string.Join(Environment.NewLine, tally.Disagreements.Take(20)));
        for (var category = 1; category <= OracleCategories.Count; category++)
        {
            tally.CategoryReached[category].ShouldBeGreaterThanOrEqualTo(PerCategory, $"category {category}");
        }

        foreach (var (item, row) in CoverageTests.SubCases)
        {
            tally.RowSeries.GetValueOrDefault(row).ShouldBeGreaterThanOrEqualTo(CoverageTests.Floor, item);
        }
    }
}
