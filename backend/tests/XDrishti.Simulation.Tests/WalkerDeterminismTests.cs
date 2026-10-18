using System.Text;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC7: same input, same bytes. Every fixture and every seeded run, walked twice (the second time on a copy of the bars), renders identical text; the rendering of the F-days and the W-rows equals the golden file checked in
/// with the tests (see <see cref="GoldenFile"/>: the engine version on the first line, regenerated on purpose with <c>XD_UPDATE_GOLDEN=1</c>).
/// </summary>
public sealed class WalkerDeterminismTests
{
    [Fact]
    public void Every_fixture_walked_twice_once_on_a_copy_of_the_bars_renders_the_same_text()
    {
        foreach (var walkCase in Cases.All)
        {
            foreach (var item in walkCase.Items)
            {
                var first = OutcomeFormatter.Render(WalkAssert.Walk(walkCase, item));
                var second = OutcomeFormatter.Render(TradeWalker.Walk(item.Candidate, [.. walkCase.Bars], walkCase.Rules));

                second.ShouldBe(first, walkCase.Name);
            }
        }
    }

    [Fact]
    public void Every_seeded_run_walked_twice_once_on_a_copy_of_the_bars_renders_the_same_text_and_counters()
    {
        foreach (var seed in Enumerable.Range(1, 100))
        {
            var run = SeededRuns.Make(seed);
            var copy = SeededRuns.Make(seed);
            var first = RenderRun(run, run.Bars);
            var second = RenderRun(copy, [.. copy.Bars]);

            second.ShouldBe(first, "seed " + seed);
        }
    }

    [Fact]
    public void The_golden_file_equals_the_rendering_of_the_fixtures_and_the_rows()
    {
        GoldenFile.Check("walker", RenderGolden());
    }

    private static string RenderRun(SeededRun run, IReadOnlyList<Domain.Market.Bars.Bar> bars)
    {
        var text = new StringBuilder();
        var counters = WalkCounters.Empty;
        foreach (var walk in run.Walks)
        {
            var result = TradeWalker.Walk(walk.Candidate, bars, walk.Rules);
            text.AppendLine(OutcomeFormatter.Render(result));
            counters = counters.Add(result.Value.ToCounters());
        }

        return text.AppendLine(OutcomeFormatter.Render(counters)).ToString();
    }

    private static string RenderGolden()
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

        return text.Append("total ").Append(OutcomeFormatter.Render(total)).Append('\n').ToString();
    }
}
