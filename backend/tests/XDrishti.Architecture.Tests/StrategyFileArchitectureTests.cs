using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The strategy file library (P3-02c, ADR 0055) is pure: it references only Strategies and Domain among the projects and the YAML parser among the packages, reads a string and touches no file, network, clock, random or reflection.
/// The Strategies library keeps Superpower as its only package (the YAML package lives here, not there).
/// </summary>
public sealed class StrategyFileArchitectureTests
{
    private static readonly Assembly StrategyFile = typeof(XDrishti.Strategies.File.StrategyFile).Assembly;

    private static readonly string[] Forbidden =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker",
        "System.IO.File", "System.IO.Directory", "System.IO.Path", "System.IO.Stream", "System.IO.FileStream", "System.Net", "System.Diagnostics.Process",
        "System.TimeProvider", "System.DateTime", "System.Environment", "System.Random", "System.Reflection", "System.Activator", "System.Threading.Tasks",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
    ];

    [Fact]
    public void The_library_references_only_strategies_and_domain_among_the_projects_and_the_yaml_parser_among_the_packages()
    {
        var referenced = StrategyFile.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referenced.Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).ShouldBeSubsetOf(["XDrishti.Domain", "XDrishti.Strategies"]);
        referenced.Where(n => !n.StartsWith("System.", StringComparison.Ordinal) && !n.StartsWith("XDrishti.", StringComparison.Ordinal) && n is not ("netstandard" or "mscorlib" or "Microsoft.CSharp"))
            .ShouldBeSubsetOf(["YamlDotNet"]);

        var project = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Strategies.File", "XDrishti.Strategies.File.csproj"));
        RepositoryFiles.ProjectReferences(project).ShouldBe(["XDrishti.Domain", "XDrishti.Strategies"]);
        project.Descendants("PackageReference").Select(r => (string?)r.Attribute("Include")).ShouldBe(["YamlDotNet"]);
        project.Descendants("PackageReference").ShouldAllBe(r => r.Attribute("Version") == null, "versions are set once, in Directory.Packages.props");
    }

    [Fact]
    public void The_library_reads_a_string_and_knows_no_file_network_clock_random_or_reflection()
    {
        var result = Types.InAssembly(StrategyFile).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_rule_sees_a_use_of_the_file_system()
    {
        Types.InAssembly(typeof(FileSentinel).Assembly).That().HaveName(nameof(FileSentinel)).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_yaml_package_is_pinned_to_an_exact_version_and_is_not_referenced_by_the_strategies_or_simulation_libraries()
    {
        var packages = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"));

        packages.Descendants("PackageVersion").Where(p => (string?)p.Attribute("Include") == "YamlDotNet").Select(p => (string?)p.Attribute("Version")).ShouldHaveSingleItem()
            .ShouldNotBeNull().ShouldMatch(@"^\d+\.\d+\.\d+$");
        typeof(XDrishti.Strategies.Problem).Assembly.GetReferencedAssemblies().ShouldNotContain(a => a.Name == "YamlDotNet");
        typeof(XDrishti.Simulation.Replay.BuiltInStrategies).Assembly.GetReferencedAssemblies().ShouldNotContain(a => a.Name == "YamlDotNet");
    }

    [Fact]
    public void The_project_is_in_the_solution_and_the_dockerfile_copies_it()
    {
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx")).ShouldContain("src/XDrishti.Strategies.File/XDrishti.Strategies.File.csproj");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "Dockerfile")).ShouldContain("COPY src/XDrishti.Strategies.File/XDrishti.Strategies.File.csproj src/XDrishti.Strategies.File/");
    }
}

/// <summary>Reads a file on purpose, to prove the purity rule sees such a use (never used by anything else).</summary>
internal static class FileSentinel
{
    public static string Read() => File.ReadAllText("x");
}
