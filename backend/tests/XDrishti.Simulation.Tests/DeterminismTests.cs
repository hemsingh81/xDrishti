using System.Text;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC7: same input, same bytes. Every fixture and every seeded run, walked twice (the second time on a copy of the bars), renders identical text; the rendering of the F-days and the W-rows equals the golden file checked in
/// with the tests, which starts with the engine version it was made with. A changed rule needs a new <see cref="SimulationEngine.Version"/> and a regenerated file, on purpose:
/// <c>XD_UPDATE_GOLDEN=1 dotnet test --project tests/XDrishti.Simulation.Tests --filter-method "*golden*"</c> rewrites it.
/// </summary>
public sealed class DeterminismTests
{
    private const string GoldenName = "walker.golden.txt";

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
        var rendered = RenderGolden();
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("XD_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(SourceGoldenPath(), rendered);
            return;
        }

        var golden = File.ReadAllText(path);

        golden.Split('\n')[0].ShouldBe("engine " + SimulationEngine.Version, "the golden file was made with another engine version: bump the version with the rule and regenerate the file");
        rendered.ShouldBe(golden, "a changed rule needs a new SimulationEngine.Version and a regenerated golden file (XD_UPDATE_GOLDEN=1)");
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
        text.Append("engine ").Append(SimulationEngine.Version).Append('\n');
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

    private static string GoldenPath() => Path.Combine(AppContext.BaseDirectory, "Golden", GoldenName);

    /// <summary>The checked-in copy, found by walking up from the test binaries (the tests run from a checkout).</summary>
    private static string SourceGoldenPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "XDrishti.Simulation.Tests.csproj");
            if (File.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "Golden", GoldenName);
            }
        }

        throw new FileNotFoundException("The test project directory was not found above " + AppContext.BaseDirectory);
    }
}
