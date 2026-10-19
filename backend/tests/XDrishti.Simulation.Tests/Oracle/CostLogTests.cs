using System.Globalization;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08 (criterion 10 for the costing): the disagreement log lists the trades and costings compared per cost generator and the numbers are the runs of this suite, so that a change of a count that is not written down fails
/// here; and the classed entries of the log (D-04, D-05) are the cases of the adapter that the log names.
/// </summary>
public sealed class CostLogTests
{
    private static string[] Lines() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Oracle", "Docs", "p3-08-disagreements.md"));

    [Fact]
    public void The_log_lists_the_trades_and_costings_compared_per_cost_generator_and_the_numbers_are_the_runs()
    {
        var rows = new Dictionary<string, string[]>();
        var inTable = false;
        foreach (var line in Lines())
        {
            if (line.StartsWith("| cost generator | trades", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (!inTable)
            {
                continue;
            }

            if (!line.StartsWith('|'))
            {
                break;
            }

            if (!line.StartsWith("|---", StringComparison.Ordinal))
            {
                var cells = line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);
                rows[cells[0]] = cells;
            }
        }

        foreach (var tally in CostCorpus.All)
        {
            rows.ContainsKey(tally.Name).ShouldBeTrue($"the log has no row for the cost generator '{tally.Name}'");
            var row = rows[tally.Name];
            row[1].ShouldBe(tally.Trades.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: trades");
            row[2].ShouldBe(tally.Cases.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: costings");
            row[3].ShouldBe(tally.Kept.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: kept");
            row[4].ShouldBe(tally.SkippedQuantity.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: skipped by quantity");
            row[5].ShouldBe(tally.SkippedCost.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: skipped by cost");
        }

        rows.Count.ShouldBe(CostCorpus.All.Count, "the log lists exactly the cost generators of the run");
    }

    [Fact]
    public void The_log_states_how_many_generated_cases_the_adapter_gave_the_engine_s_bound_instead_of_the_limit()
    {
        var clamped = CostCorpus.SeriesRun.Row(CostTally.LimitClamped);

        clamped.ShouldBeGreaterThan(0, "the author's generator uses a limit of 1000, which the engine refuses (entry D-04)");
        Lines().ShouldContain(l => l.Contains("D-04", StringComparison.Ordinal) && l.Contains(clamped.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal), "D-04 states the count");
        CostCorpus.ChainRun.Row(CostTally.LimitClamped).ShouldBe(0);
    }
}
