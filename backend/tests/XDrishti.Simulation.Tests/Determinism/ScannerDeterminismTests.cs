using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Determinism;

/// <summary>
/// Criterion 15 of P3-03a (the scanner's part): same input, same bytes. Every fixture and every seeded series, run twice (the second time on a copy of the bars), renders identical text for the candidates with their traces and
/// the counters; the rendering of the fixtures equals the golden file checked in with the tests (see <see cref="GoldenFile"/>: the engine version on the first line, regenerated on purpose with <c>XD_UPDATE_GOLDEN=1</c>).
/// </summary>
public sealed class ScannerDeterminismTests
{
    private static string RenderAll(IEnumerable<Fixture> fixtures, bool copyBars)
    {
        var text = new StringBuilder();
        foreach (var fixture in fixtures)
        {
            var bars = copyBars ? (Bar[])fixture.Bars.Clone() : fixture.Bars;
            var scanners = Scan.Both(fixture.Definition, bars, fixture.Timeframe);
            foreach (var scanner in scanners)
            {
                text.Append("== ").Append(fixture.Name).Append(' ').Append(scanner.Counters.Side).Append(" ==\n").Append(Render.Scanner(scanner)).Append('\n');
            }
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
        var seeded = Enumerable.Range(1, 100).Select(i => new Fixture("seed " + i, XDrishti.Strategies.BuiltIn.OrbV1.Create(), Timeframe.M15, SeededSeries.Of((ulong)i))).ToList();

        RenderAll(seeded, copyBars: false).ShouldBe(RenderAll(seeded, copyBars: true));
    }

    [Fact]
    public void The_rendering_of_the_fixtures_equals_the_golden_file()
    {
        GoldenFile.Check("scanner", RenderAll(Fixtures.All, copyBars: false));
    }
}
