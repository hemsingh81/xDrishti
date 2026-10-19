namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Acceptance criterion 12 of P3-08: the same seeds give byte-identical summaries on two runs (the generators are SplitMix64 and nothing else; the series are compared in parallel and folded in a fixed order).</summary>
public sealed class ReproducibilityTests
{
    [Fact]
    public void Two_runs_of_the_authors_generator_give_the_same_summary_text()
    {
        var first = Corpus.RunOracleSeries(_ => 40).Summary();
        var second = Corpus.RunOracleSeries(_ => 40).Summary();

        second.ShouldBe(first);
        first.Length.ShouldBeGreaterThan(200);
    }

    [Fact]
    public void Two_runs_over_an_engine_generator_give_the_same_summary_text()
    {
        string Run() => Summarise(ComparisonCases.Swing(exact: false).Take(60).Select(c => SeriesComparison.Run(c, new RatioStats())));

        Run().ShouldBe(Run());
    }

    [Fact]
    public void A_different_seed_gives_a_different_summary_so_the_check_can_fail()
    {
        static string Sample(int offset) => Summarise(Enumerable.Range(0, 30).SelectMany(s => Enumerable.Range(1, OracleCategories.Count).Select(c => SeriesComparison.Run(ComparisonCases.OfOracle(OracleSeries.Of(c, s + offset), exact: false), new RatioStats()))));

        Sample(1000).ShouldNotBe(Sample(0));
    }

    private static string Summarise(IEnumerable<SeriesResult> results)
    {
        var tally = new Tally("sample");
        foreach (var result in results)
        {
            tally.Add(result);
        }

        return tally.Summary();
    }
}
