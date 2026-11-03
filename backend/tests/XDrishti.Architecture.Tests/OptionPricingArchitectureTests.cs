using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-05 criterion 9 (ADR 0191): the option pricing core is pure. <c>Domain/Options/Pricing</c> depends on nothing outside <c>System</c> and <c>Domain/Common</c> (no I/O, no clock, no random source, no other Domain area), its types are public, sealed or enum and one per file,
/// and no decision layer, live feed, recorder or the execution service references it in this slice (P14-07 and later loosen this with an exact allow-list).
/// </summary>
public sealed class OptionPricingArchitectureTests
{
    private const string Pricing = "XDrishti.Domain.Options.Pricing";
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static string Dir => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Options", "Pricing", "Black76.cs"))!;

    [Fact]
    public void The_pricing_namespace_holds_types_and_depends_only_on_system_and_domain_common()
    {
        var own = Domain.GetTypes().Where(t => t.Namespace == Pricing).ToList();
        own.Count.ShouldBeGreaterThan(10);
        // A namespace prefix would also match Pricing itself when it is an ancestor ("XDrishti.Domain.Options"), so the ancestors are banned by their exact type names.
        var others = Domain.GetTypes().Where(t => t.Namespace is not null && t.Namespace != Pricing && !t.Namespace.StartsWith("XDrishti.Domain.Common", StringComparison.Ordinal)).ToList();
        var otherDomain = others.Where(t => !Pricing.StartsWith(t.Namespace!, StringComparison.Ordinal)).Select(t => t.Namespace!).Distinct()
            .Concat(others.Where(t => Pricing.StartsWith(t.Namespace!, StringComparison.Ordinal) && !t.Name.Contains('<', StringComparison.Ordinal)).Select(t => t.FullName!)).ToArray();
        string[] banned = [.. otherDomain, "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Security", "System.Threading.Tasks", "System.Reflection", "System.Text.Json", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment"];

        var result = Types.InAssembly(Domain).That().ResideInNamespace(Pricing).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_rule_is_not_vacuous_a_scan_sees_a_dependency_where_there_is_one()
    {
        Types.InAssembly(Domain).That().ResideInNamespace(Pricing).And().HaveName("Black76").ShouldNot().HaveDependencyOn("System").GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Options.Quality").ShouldNot().HaveDependencyOn("System.Linq").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_pricing_source_reads_no_clock_no_random_source_and_no_shared_state()
    {
        foreach (var file in Directory.GetFiles(Dir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Regex.IsMatch(text, @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|Environment\.|File\.|Stopwatch|static\s+(?!readonly|class|double|bool|int|string)\w+\s+_)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_pricing_type_is_public_and_sealed_or_static_and_declared_in_a_file_of_its_name()
    {
        var types = Domain.GetTypes().Where(t => t.Namespace == Pricing && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal) && !t.Name.Contains('`', StringComparison.Ordinal)).ToList();
        foreach (var type in types)
        {
            (type.IsPublic || type.Name == "PricingInputs").ShouldBeTrue(type.Name);
            type.IsSealed.ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(Dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }
    }

    [Theory]
    [InlineData("XDrishti.Domain.Selection")]
    [InlineData("XDrishti.Domain.Planning")]
    [InlineData("XDrishti.Domain.Checklist")]
    [InlineData("XDrishti.Domain.LiveTrading")]
    [InlineData("XDrishti.Domain.Paper")]
    [InlineData("XDrishti.Application")]
    [InlineData("XDrishti.Infrastructure")]
    public void No_decision_or_application_namespace_references_the_pricing_core(string area)
    {
        var assembly = area.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Assembly.Load(new AssemblyName(area));
        var types = area.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Types.InAssembly(assembly).That().ResideInNamespaceStartingWith(area) : Types.InAssembly(assembly).That().DoNotResideInNamespaceStartingWith(Pricing);

        var result = types.ShouldNot().HaveDependencyOn(Pricing).GetResult();

        result.IsSuccessful.ShouldBeTrue(area + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_live_feed_the_worker_the_api_the_simulator_the_strategies_and_the_execution_service_reference_no_pricing_type()
    {
        foreach (var name in new[] { "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Execution", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Learning.MlNet", "XDrishti.Hosting" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOn(Pricing).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void No_other_domain_area_references_the_pricing_core_yet()
    {
        // P14-08 (ADR 0197): the option re-mark is the first Domain code to price an option; the exact allow-list is that one namespace.
        var result = Types.InAssembly(Domain).That().DoNotResideInNamespace(Pricing).And().DoNotResideInNamespace("XDrishti.Domain.Options.Remark").ShouldNot().HaveDependencyOn(Pricing).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
