using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Criterion 22 of P3-03c (ADR 0042): costs and sizing are pure Domain code that one place of the simulation library calls. The new Domain types are public, sealed and one per file; the walker, the simulator and the scanner do not know them;
/// only the replayer calls <c>TradeCostApplier</c>; the TEST charge sheet is in no <c>src/</c> assembly; no project, package or migration was added. Each rule has a sentinel: a case in this test project that the same code must reject, so that
/// a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed partial class CostArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    /// <summary>The public types this slice adds to <c>XDrishti.Domain.Research</c> (listed in <c>DomainResearchSurfaceTests</c> too).</summary>
    private static readonly string[] NewTypes =
    [
        "ChargeRates", "ChargeRounding", "ChargeSchedule", "CostApplication", "CostContext", "CostDescription", "CostErrors", "CostLimits", "CostModel", "CostRecord", "CostSegment", "CostSheet", "CostSheetJson", "LegCharges", "Sizing", "SizingProfile",
        "TradeCostApplier", "TradeCosting", "TradeSkip",
    ];

    private static readonly string[] CostTypeNames =
    [
        "XDrishti.Domain.Research.CostSheet", "XDrishti.Domain.Research.CostModel", "XDrishti.Domain.Research.Sizing", "XDrishti.Domain.Research.TradeCosting", "XDrishti.Domain.Research.TradeCostApplier", "XDrishti.Domain.Research.CostContext",
        "XDrishti.Domain.Research.CostRecord", "XDrishti.Domain.Research.ChargeRates",
    ];

    private static string SourceRoot => Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "src");

    [GeneratedRegex("^(Cost|Charge|Sizing|Trade|LegCharges)", RegexOptions.CultureInvariant)]
    private static partial Regex CostFamily();

    [GeneratedRegex(@"^\s*public\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:record\s+struct|record\s+class|record|class|struct|enum|interface)\s", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PublicTypeDeclaration();

    // (a) the new Domain types

    [Fact]
    public void The_new_domain_types_are_public_sealed_or_enums_in_the_research_namespace_and_one_per_file()
    {
        var exported = Domain.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Domain.Research").ToDictionary(t => t.Name);

        foreach (var name in NewTypes)
        {
            exported.ShouldContainKey(name);
            var type = exported[name];
            (type.IsEnum || type.IsSealed).ShouldBeTrue(name + " is sealed (a static class is sealed and abstract) or an enum");
            var file = Path.Combine(SourceRoot, "XDrishti.Domain", "Research", name + ".cs");
            File.Exists(file).ShouldBeTrue(name + " has a file of its own");
            PublicTypeDeclaration().Count(File.ReadAllText(file)).ShouldBe(1, name + " is the only public type of its file");
        }
    }

    [Fact]
    public void The_helpers_of_the_sheet_are_internal_and_no_other_public_cost_type_appeared()
    {
        Domain.GetType("XDrishti.Domain.Research.CostField")!.IsPublic.ShouldBeFalse();
        Domain.GetType("XDrishti.Domain.Research.MiniJson")!.IsPublic.ShouldBeFalse();
        Domain.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Domain.Research" && CostFamily().IsMatch(t.Name) && t.Name is not ("CostBasis" or "TradeCardData" or "TradeExplanation"))
            .Select(t => t.Name).Order().ShouldBe([.. NewTypes.Order()]);
    }

    [Fact]
    public void The_cost_types_do_no_io_read_no_clock_and_draw_no_random_number_and_the_rule_does_see_a_violation()
    {
        var result = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Research").And().HaveNameMatching("^(Cost|Charge|Sizing|Trade|LegCharges|MiniJson)")
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics", "System.TimeProvider", "System.Environment", "System.Random", "System.Text.Json", "System.Security.Cryptography.RandomNumberGenerator")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(typeof(CostClockSentinel).Assembly).That().HaveName(nameof(CostClockSentinel)).ShouldNot().HaveDependencyOnAny("System.Random", "System.IO").GetResult().IsSuccessful.ShouldBeFalse("the sentinel draws a random number and reads a file");
        Types.InAssembly(Domain).That().HaveName("CanonicalText").ShouldNot().HaveDependencyOnAny("System.Security.Cryptography").GetResult().IsSuccessful.ShouldBeFalse("the scan sees a dependency where there is one");
    }

    // (b) the walker, the simulator and the scanner do not know costs

    [Fact]
    public void The_walking_and_scanning_namespaces_and_the_simulator_refer_to_no_cost_type()
    {
        foreach (var @namespace in new[] { "XDrishti.Simulation.Walking", "XDrishti.Simulation.Scanning" })
        {
            var result = Types.InAssembly(Simulation).That().ResideInNamespace(@namespace).ShouldNot().HaveDependencyOnAny(CostTypeNames).GetResult();

            result.IsSuccessful.ShouldBeTrue(@namespace + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        var simulator = Types.InAssembly(Simulation).That().HaveName("Simulator").ShouldNot().HaveDependencyOnAny(CostTypeNames).GetResult();
        simulator.IsSuccessful.ShouldBeTrue("Simulator violations: " + string.Join(", ", simulator.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_rule_does_see_a_dependency_on_a_cost_type_where_there_is_one()
    {
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").ShouldNot().HaveDependencyOnAny(CostTypeNames).GetResult().IsSuccessful.ShouldBeFalse("the replayer uses TradeCostApplier");
        Types.InAssembly(typeof(CostClockSentinel).Assembly).That().HaveName(nameof(CostApplierSentinel)).ShouldNot().HaveDependencyOnAny(CostTypeNames).GetResult().IsSuccessful.ShouldBeFalse();
    }

    // (c) only the replayer calls the applier

    private static List<string> CallersOfTheApplier(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(t => t.DeclaringType is null && !t.Name.StartsWith('<'))
            .Where(t => AssemblyScan.MembersUsedBy(assembly, t.FullName!).Any(m => m is { Type: "XDrishti.Domain.Research.TradeCostApplier", Member: "Apply" }))
            .Select(t => t.FullName!)];

    [Fact]
    public void The_replayer_is_the_only_code_of_the_simulation_library_that_calls_the_applier()
    {
        CallersOfTheApplier(Simulation).ShouldBe(["XDrishti.Simulation.Replay.InstrumentReplayer"]);
    }

    [Fact]
    public void The_caller_rule_does_see_a_call_of_the_applier_in_a_sentinel()
    {
        CallersOfTheApplier(typeof(CostApplierSentinel).Assembly).ShouldContain(typeof(CostApplierSentinel).FullName);
    }

    // (d) the TEST sheet is test code

    [Fact]
    public void The_test_charge_sheet_is_in_no_src_assembly_and_no_src_file_names_it()
    {
        var assemblies = new[] { Domain, Simulation, typeof(XDrishti.SimExport.ExportSpellings).Assembly, typeof(XDrishti.Application.DependencyInjection).Assembly, typeof(XDrishti.Infrastructure.DependencyInjection).Assembly };
        foreach (var assembly in assemblies)
        {
            assembly.GetTypes().ShouldNotContain(t => t.Name == "TestCostSheet", assembly.GetName().Name);
        }

        var offending = Directory.GetFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && (Path.GetFileName(f) == "TestCostSheet.cs" || File.ReadAllText(f).Contains("test-synthetic", StringComparison.Ordinal)))
            .ToList();

        offending.ShouldBeEmpty();
    }

    [Fact]
    public void The_test_sheet_rule_does_see_the_sheet_in_the_shared_test_code()
    {
        var shared = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "tests", "Shared");

        File.Exists(Path.Combine(shared, "TestCostSheet.cs")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(shared, "TestCostSheet.cs")).ShouldContain("test-synthetic-1");
        Domain.GetTypes().ShouldContain(t => t.Name == "CostSheet", "the type scan sees a type of the assembly by name");
    }

    // (e) nothing else was added

    [Fact]
    public void No_project_package_or_migration_was_added_by_the_slice()
    {
        var projects = Directory.GetFiles(SourceRoot, "*.csproj", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)).Select(Path.GetFileNameWithoutExtension).Order().ToList();
        var migrations = Directory.GetFiles(Path.Combine(SourceRoot, "XDrishti.Infrastructure", "Persistence", "Migrations"), "*.cs").Select(Path.GetFileName).ToList();

        projects.ShouldBe(
        [
            "XDrishti.Api", "XDrishti.Application", "XDrishti.Domain", "XDrishti.Feed", "XDrishti.Hosting", "XDrishti.Indicators", "XDrishti.Infrastructure", "XDrishti.Learning.MlNet", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Worker",
        ]);
        migrations.ShouldNotContain(m => m!.Contains("Cost", StringComparison.OrdinalIgnoreCase) || m.Contains("Charge", StringComparison.OrdinalIgnoreCase), "the store of the sheet and its migration are P3-03f");
    }
}

/// <summary>Draws a random number and reads a file on purpose, to prove the purity rule of the cost types sees such a use (it is never used by anything else).</summary>
internal static class CostClockSentinel
{
    public static int Draw() => new Random().Next();

    public static string Read() => File.ReadAllText("x");
}

/// <summary>Calls the trade costing on purpose, to prove the rules about who may call it see a call (it is never used by anything else).</summary>
internal static class CostApplierSentinel
{
    public static object Use(XDrishti.Domain.Research.SimTrade trade, XDrishti.Domain.Research.CostContext context) =>
        XDrishti.Domain.Research.TradeCostApplier.Apply(trade, context, XDrishti.Domain.Research.CostSegment.IntradayEquity);
}
