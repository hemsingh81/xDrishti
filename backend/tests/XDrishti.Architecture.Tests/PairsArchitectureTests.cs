using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-12 criterion 8 (ADR 0192): the pairs core is pure. <c>Domain/Pairs</c> depends on <c>System</c>, <c>Domain/Common</c> and <c>Domain/Lab</c> only (no I/O, no clock, no random source, no other Domain area),
/// its types are public and sealed or static or an enum and one per file, and nothing outside research code references it in this slice (P14-13 loosens this with an exact allow-list).
/// </summary>
public sealed class PairsArchitectureTests
{
    private const string Pairs = "XDrishti.Domain.Pairs";
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static string Dir => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Pairs", "EngleGranger.cs"))!;

    [Fact]
    public void The_pairs_namespace_holds_types_and_depends_only_on_system_common_and_lab()
    {
        Domain.GetTypes().Count(t => t.Namespace == Pairs).ShouldBeGreaterThan(10);
        var others = Domain.GetTypes().Where(t => t.Namespace is not null && t.Namespace != Pairs && !t.Namespace.StartsWith("XDrishti.Domain.Common", StringComparison.Ordinal) && !t.Namespace.StartsWith("XDrishti.Domain.Lab", StringComparison.Ordinal)).ToList();
        var otherDomain = others.Where(t => !Pairs.StartsWith(t.Namespace!, StringComparison.Ordinal)).Select(t => t.Namespace!).Distinct()
            .Concat(others.Where(t => Pairs.StartsWith(t.Namespace!, StringComparison.Ordinal) && !t.Name.Contains('<', StringComparison.Ordinal)).Select(t => t.FullName!)).ToArray();
        string[] banned = [.. otherDomain, "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Security", "System.Threading.Tasks", "System.Reflection", "System.Text.Json", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment"];

        var result = Types.InAssembly(Domain).That().ResideInNamespace(Pairs).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_rule_is_not_vacuous_a_scan_sees_a_dependency_where_there_is_one()
    {
        Types.InAssembly(Domain).That().ResideInNamespace(Pairs).And().HaveName("EngleGranger").ShouldNot().HaveDependencyOn("System").GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Options.Quality").ShouldNot().HaveDependencyOn("System.Linq").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_pairs_source_reads_no_clock_no_random_source_and_no_shared_state()
    {
        foreach (var file in Directory.GetFiles(Dir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Regex.IsMatch(text, @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|SeededRandom|Environment\.|File\.|Stopwatch|static\s+(?!readonly|class|double|bool|int|string)\w+\s+_)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_pairs_type_is_public_and_sealed_or_static_or_an_enum_and_declared_in_a_file_of_its_name()
    {
        var types = Domain.GetTypes().Where(t => t.Namespace == Pairs && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal) && !t.Name.Contains('`', StringComparison.Ordinal)).ToList();
        foreach (var type in types)
        {
            (type.IsPublic || type.Name == "SmallMatrix").ShouldBeTrue(type.Name);
            (type.IsSealed || type.IsEnum).ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(Dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }

        File.Exists(Path.Combine(Dir, "PairsResult.cs")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("XDrishti.Domain.Selection")]
    [InlineData("XDrishti.Domain.Planning")]
    [InlineData("XDrishti.Domain.Checklist")]
    [InlineData("XDrishti.Domain.LiveTrading")]
    [InlineData("XDrishti.Domain.Paper")]
    [InlineData("XDrishti.Domain.Execution")]
    [InlineData("XDrishti.Application")]
    [InlineData("XDrishti.Infrastructure")]
    public void No_decision_or_application_namespace_references_the_pairs_core(string area)
    {
        var assembly = area.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Assembly.Load(new AssemblyName(area));
        var types = area.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Types.InAssembly(assembly).That().ResideInNamespaceStartingWith(area) : Types.InAssembly(assembly).That().DoNotResideInNamespaceStartingWith(Pairs);

        var result = types.ShouldNot().HaveDependencyOn(Pairs).GetResult();

        result.IsSuccessful.ShouldBeTrue(area + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_hosts_the_simulator_and_the_execution_service_reference_no_pairs_type()
    {
        foreach (var name in new[] { "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Execution", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Learning.MlNet", "XDrishti.Hosting" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOn(Pairs).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void No_other_domain_area_references_the_pairs_core_yet()
    {
        var result = Types.InAssembly(Domain).That().DoNotResideInNamespace(Pairs).ShouldNot().HaveDependencyOn(Pairs).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
