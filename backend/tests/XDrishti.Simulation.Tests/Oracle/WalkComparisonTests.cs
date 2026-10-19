namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criteria 5 and 6 of P3-08: over O's <c>OracleSeries</c> and the engine's four generator families (in both fill modes), the oracle and the engine's walker (and gate) agree on every field of D4 under D5; every disagreement is
/// in the log (<c>DisagreementLogTests</c> holds the log to this run); the largest ratio difference seen is printed.
/// </summary>
public sealed class WalkComparisonTests(ITestOutputHelper output)
{
    [Fact]
    public void Summary_of_the_run()
    {
        var lines = new List<string>();
        foreach (var tally in Corpus.All)
        {
            lines.Add($"{tally.Name}: series {tally.Series}, comparisons {tally.Comparisons}, candidates {tally.Candidates}, trades {tally.Trades}, blocked {tally.Blocked}, gated series {tally.GatedSeries}, disagreements {tally.DisagreementCount}");
        }

        lines.Add($"ratio fields: {Corpus.Stats.Compared} compared, {Corpus.Stats.NotExact} not bit-equal, largest difference {Corpus.Stats.MaxDifference:R} {Corpus.Stats.Worst}");
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        foreach (var tally in Corpus.All)
        {
            foreach (var d in tally.Disagreements.Take(40))
            {
                output.WriteLine(d);
            }
        }

        Report.Write("summary.txt", lines);
        Corpus.All.Sum(t => t.DisagreementCount).ShouldBe(0, string.Join(Environment.NewLine, Corpus.All.SelectMany(t => t.Disagreements.Take(10))));
    }
}
