using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Determinism;

/// <summary>
/// Criterion 15 of P3-03a for <c>Simulator.Run</c>: same input, same bytes. Every fixture and every seeded series, run twice (the second time on a copy of the bars), renders identical text for the trades, the candidates with
/// their traces and the counters of both stages; the rendering of the fixtures equals the golden file <c>simulator.golden.txt</c> (see <see cref="GoldenFile"/>: the engine version on the first line, regenerated on purpose with
/// <c>XD_UPDATE_GOLDEN=1</c>). The strategy of F22 is one the simulator refuses by name (a swing-low stop is run on the scanner only), so its place in the file is the refusal.
/// </summary>
public sealed class SimulatorDeterminismTests
{
    private static string RenderAll(IEnumerable<Fixture> fixtures, bool copyBars)
    {
        var text = new StringBuilder();
        foreach (var fixture in fixtures)
        {
            var bars = copyBars ? (Bar[])fixture.Bars.Clone() : fixture.Bars;
            text.Append("== ").Append(fixture.Name).Append(" ==\n");
            var build = Simulator.Create(fixture.Definition, fixture.Timeframe);
            if (build.Simulator is null)
            {
                text.Append("refused ").Append(string.Join("; ", build.Problems.Select(p => p.Problem.Code + " at " + p.Path))).Append('\n');
                continue;
            }

            text.Append(Render.Result(Sim.Run(build.Simulator, bars)));
        }

        return text.ToString();
    }

    [Fact]
    public void The_fixtures_render_the_same_text_twice_the_second_time_on_a_copy_of_the_bars()
    {
        RenderAll(Fixtures.All, copyBars: false).ShouldBe(RenderAll(Fixtures.All, copyBars: true));
    }

    [Fact]
    public void The_seeded_series_render_the_same_text_twice()
    {
        var seeded = Enumerable.Range(1, 100).Select(i => new Fixture("seed " + i, OrbV1.Create(), Timeframe.M15, SeededSeries.Of((ulong)i))).ToList();

        RenderAll(seeded, copyBars: false).ShouldBe(RenderAll(seeded, copyBars: true));
    }

    [Fact]
    public void Every_fixture_strategy_is_run_now_that_the_level_stops_are_lifted_F22_was_the_only_one_refused_before()
    {
        var refused = Fixtures.All.Where(f => Simulator.Create(f.Definition, f.Timeframe).Simulator is null).Select(f => f.Name);

        refused.ShouldBeEmpty();
    }

    [Fact]
    public void The_rendering_of_the_fixtures_equals_the_golden_file()
    {
        GoldenFile.Check("simulator", RenderAll(Fixtures.All, copyBars: false));
    }
}
