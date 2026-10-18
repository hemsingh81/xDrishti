using System.Reflection;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Determinism;

/// <summary>
/// Criterion 15 of P3-03a (the scanner's part): same input, same bytes. Every fixture and every seeded series, run twice (the second time on a copy of the bars), renders identical text for the candidates with their traces and
/// the counters; the rendering of the fixtures equals the golden file checked in with the tests, which starts with the engine version it was made with. The golden file is regenerated on purpose (run the explicit test
/// <c>Regenerate_the_golden_file</c>) when a rule changes, and a changed rule needs a new <see cref="SimulationEngine.Version"/>.
/// </summary>
public sealed class DeterminismTests
{
    private const string Resource = "Golden/scanner.golden.txt";

    private static string RenderAll(IEnumerable<Fixture> fixtures, bool copyBars)
    {
        var text = new StringBuilder("engine: " + SimulationEngine.Version + "\n");
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
    public void The_rendering_of_the_fixtures_equals_the_golden_file_and_the_file_starts_with_the_engine_version()
    {
        var golden = Golden();

        golden.ShouldStartWith("engine: " + SimulationEngine.Version + "\n", Case.Sensitive, "the golden file was made with another engine version: regenerate it on purpose, and raise the version when a rule changed");
        RenderAll(Fixtures.All, copyBars: false).ShouldBe(golden, "the scanner said something else than the golden file: if a rule changed on purpose, raise SimulationEngine.Version and run Regenerate_the_golden_file");
    }

    [Fact(Explicit = true)]
    public void Regenerate_the_golden_file()
    {
        File.WriteAllText(SourceFile(), RenderAll(Fixtures.All, copyBars: false));
    }

    private static string Golden()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource) ?? throw new InvalidOperationException("The golden file is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string SourceFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Determinism", "scanner.golden.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The golden file was not found above " + AppContext.BaseDirectory);
    }
}
