using System.Reflection;
using System.Xml.Linq;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P3-09c AC 2: the chart reader is written from the protocol, not from the engine, so it shares no code with it: its project file references nothing, its assembly references no other xDrishti assembly,
/// its sources name neither the engine, the oracle nor the export, and no test of the reader reads the engine's values file.
/// </summary>
public sealed class SimReaderArchitectureTests
{
    private const string ReaderName = "XDrishti.SimReader";

    private static readonly string[] Forbidden = ["XDrishti.Simulation", "XDrishti.SimOracle", "XDrishti.SimExport"];

    private static string Folder(string tier, string name) => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", tier, name, name + ".csproj"))!;

    private static IEnumerable<string> Sources(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f));

    [Fact]
    public void The_reader_project_references_no_project_and_no_package()
    {
        var project = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "src", ReaderName, ReaderName + ".csproj"));

        RepositoryFiles.ProjectReferences(project).ShouldBeEmpty();
        project.Descendants("PackageReference").ShouldBeEmpty();
    }

    [Fact]
    public void The_reader_assembly_references_no_xdrishti_assembly()
    {
        var assembly = Assembly.Load(new AssemblyName(ReaderName));

        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void No_reader_source_names_the_engine_the_oracle_or_the_export()
    {
        foreach (var file in Sources(Folder("src", ReaderName)))
        {
            var text = File.ReadAllText(file);
            foreach (var name in Forbidden)
            {
                text.Contains(name, StringComparison.Ordinal).ShouldBeFalse(file + " names " + name);
            }
        }
    }

    [Fact]
    public void No_reader_test_reads_the_engine_values_file()
    {
        // the name is built from two pieces so that this file does not contain it itself
        var name = "engine-" + "values.csv";
        var files = Sources(Folder("tests", ReaderName + ".Tests")).ToList();

        files.ShouldNotBeEmpty();
        files.Where(f => File.ReadAllText(f).Contains(name, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    [Fact]
    public void The_scan_does_reject_a_text_that_names_the_engine_values_file()
    {
        // sentinel: the scan used above finds the name when it is there
        ("var path = \"data/exports/verify/x/" + "engine-" + "values.csv\";").Contains("engine-" + "values.csv", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public void The_source_rule_does_reject_a_text_that_names_the_engine()
    {
        // sentinel: the check used above flags a text that names a forbidden project
        const string text = "using XDrishti.Simulation.Walking;";

        Forbidden.Any(n => text.Contains(n, StringComparison.Ordinal)).ShouldBeTrue();
    }
}
