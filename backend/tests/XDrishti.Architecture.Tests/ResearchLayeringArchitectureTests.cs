using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// ADR 0039 (P3-04a, criterion 12): an engine module is reached from an adapter in Infrastructure, never from Application. Application references the Domain only; the port <c>ISimulationEngine</c> names Domain values only; the use case
/// <c>RunSimulationHandler</c> takes ports only; Infrastructure alone references the simulation library, and its adapter does no I/O, reads no clock and touches the Strategies library only through the located problem. Each rule has a
/// sentinel, a case that the same code must reject, so that a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed class ResearchLayeringArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    private static readonly string[] EngineModules = ["XDrishti.Simulation", "XDrishti.Indicators", "XDrishti.Strategies"];

    private static readonly string[] IoAndClock =
    [
        "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics", "System.TimeProvider", "System.Environment", "System.Random",
        "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Extensions.Http", "Microsoft.Extensions.Logging",
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static XDocument Project(string name) => XDocument.Load(RepositoryFiles.RepositoryFile("backend", "src", name, name + ".csproj"));

    // (a) Application

    [Fact]
    public void Application_references_the_domain_only_among_the_projects_in_its_project_file_and_in_the_compiled_assembly()
    {
        RepositoryFiles.ProjectReferences(Project("XDrishti.Application")).ShouldBe(["XDrishti.Domain"]);
        Application.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).Distinct().ShouldBe(["XDrishti.Domain"]);
    }

    [Fact]
    public void No_type_of_application_depends_on_an_engine_module()
    {
        ShouldHaveNone(Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(EngineModules).GetResult());
    }

    [Fact]
    public void The_port_of_the_engine_names_domain_values_only_and_no_engine_type()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Abstractions.Research").GetTypes().ToList();

        types.Where(t => t.IsInterface).Select(t => t.Name).Order().ShouldBe(["ICostSheetStore", "IExitSelectionStore", "IPreparedStrategy", "IRunStore", "ISimulationEngine", "IStrategyStore"]);  // the strategy store (P3-02d) is the fifth; the store of the charge sheet (P3-03f) and the result store (P3-10) are the third and fourth ports of the research family
        types.Where(t => !t.IsInterface).Select(t => t.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["AppendResult", "ComparisonMemberToStore", "ComparisonRecordText", "ComparisonToStore", "ExitSelectionFoldToStore", "ExitSelectionToStore", "NewVersion", "PageRequest", "Page`1", "RunListFilter", "RunRecordText", "RunToStore", "StoreErrors", "StoreOutcome", "StoreReadException", "StoredComparisonHeader", "StoredRunHeader", "StoredStrategy", "StoredVersion", "StrategyDescription", "StrategyStoreErrors", "TradePageFilter"]); // the rest are the values and failures of the result store port (P3-10, and the comparison of P4-05)
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Abstractions.Research").ShouldNot().HaveDependencyOnAny(EngineModules).GetResult());
    }

    [Fact]
    public void The_rules_of_application_do_catch_a_type_that_names_an_engine_module()
    {
        // the sentinel (this project) names a type of the simulation library, exactly what Application must never do; the same rule fails on it
        Types.InAssembly(typeof(EngineUserSentinel).Assembly).That().HaveName(nameof(EngineUserSentinel)).ShouldNot().HaveDependencyOnAny(EngineModules).GetResult().IsSuccessful.ShouldBeFalse();
        // and the project-file rule can see more than one reference: the infrastructure project has several
        RepositoryFiles.ProjectReferences(Project("XDrishti.Infrastructure")).Count.ShouldBeGreaterThan(1);
    }

    // (b) Infrastructure and the references to the library

    [Fact]
    public void Infrastructure_references_application_and_the_simulation_library_only_and_reaches_strategies_transitively()
    {
        var references = RepositoryFiles.ProjectReferences(Project("XDrishti.Infrastructure"));

        references.Order().ShouldBe(["XDrishti.Application", "XDrishti.Learning.MlNet", "XDrishti.Simulation"]); // the ML.NET adapter is the one other reference (P6-07, ADR 0070): Infrastructure implements the model ports over it
        references.ShouldNotContain("XDrishti.Strategies", "the compiler warm-up rule looks for a direct reference to the Strategies library; the adapter reaches it through the located problem only");
        references.ShouldNotContain("XDrishti.Indicators");
    }

    [Fact]
    public void Nothing_but_infrastructure_in_src_references_the_simulation_library_and_the_scan_does_see_references()
    {
        var sourceRoot = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "src");
        var users = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !RepositoryFiles.IsBuildOutput(file) && RepositoryFiles.ProjectReferences(XDocument.Load(file)).Contains("XDrishti.Simulation"))
            .Select(Path.GetFileNameWithoutExtension)
            .Order()
            .ToList();

        users.ShouldBe(["XDrishti.Infrastructure"]);

        // the test projects may reference it (the simulation tests and this one): the same scan finds them, so a rule that finds nothing would be noticed
        var testRoot = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "tests");
        Directory.GetFiles(testRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !RepositoryFiles.IsBuildOutput(file) && RepositoryFiles.ProjectReferences(XDocument.Load(file)).Contains("XDrishti.Simulation"))
            .Select(Path.GetFileNameWithoutExtension)
            .ShouldContain("XDrishti.Simulation.Tests");
    }

    [Fact]
    public void The_dockerfile_copies_the_project_file_of_every_project_infrastructure_needs()
    {
        var dockerfile = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "Dockerfile"));

        dockerfile.ShouldContain("COPY src/XDrishti.Simulation/XDrishti.Simulation.csproj src/XDrishti.Simulation/");
        dockerfile.ShouldContain("COPY src/XDrishti.Strategies/XDrishti.Strategies.csproj src/XDrishti.Strategies/");
        dockerfile.ShouldContain("COPY src/XDrishti.Indicators/XDrishti.Indicators.csproj src/XDrishti.Indicators/");
    }

    // (c) the use case

    [Fact]
    public void The_use_case_is_internal_and_sealed_and_takes_ports_the_time_provider_and_a_logger_only()
    {
        var handler = Application.GetType("XDrishti.Application.Research.RunSimulationHandler");

        handler.ShouldNotBeNull();
        handler.IsPublic.ShouldBeFalse();
        handler.IsSealed.ShouldBeTrue();
        var parameters = handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
        parameters.ShouldNotBeEmpty();
        parameters.ShouldAllBe(t => t.IsInterface || t == typeof(TimeProvider), "ports are interfaces; time comes from the TimeProvider");
        parameters.Select(t => t.Name).ShouldContain("ISimulationEngine");
        parameters.Select(t => t.Name).ShouldContain("IBarStore");
        parameters.Select(t => t.Name).ShouldContain("ILogger`1");
    }

    /// <summary>What the code of the type must not touch: the wall clock, the environment, random numbers, files, the network, tick counters.</summary>
    private static List<string> ClockAndAmbientUses(Assembly assembly, string typeFullName) =>
        [.. AssemblyScan.MembersUsedBy(assembly, typeFullName)
            .Where(c => (c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today")
                || c.Type is "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.Diagnostics.Stopwatch" or "System.Environment" or "System.Random"
                || c.Type.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) && c.Member is "Run" or "Delay")
            .Select(c => c.Type + "." + c.Member)
            .Distinct()];

    [Fact]
    public void The_use_case_reads_no_clock_other_than_through_the_time_provider()
    {
        var handler = Application.GetType("XDrishti.Application.Research.RunSimulationHandler")!;

        ClockAndAmbientUses(Application, handler.FullName!).ShouldBeEmpty();
        AssemblyScan.MembersUsedBy(Application, handler.FullName!).ShouldContain(c => c.Type == "System.TimeProvider", "the scan sees the one clock the handler may read");
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Research").ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Random", "System.Environment", "Microsoft.EntityFrameworkCore", "Npgsql").GetResult());
        handler.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(f => f.FieldType).ShouldContain(typeof(TimeProvider));
    }

    [Fact]
    public void The_clock_scan_does_catch_a_type_that_reads_the_wall_clock_or_a_file()
    {
        ClockAndAmbientUses(typeof(IoUserSentinel).Assembly, typeof(IoUserSentinel).FullName!).ShouldBe(["System.IO.File.ReadAllText", "System.DateTime.get_UtcNow"], ignoreOrder: true);
    }

    // (d) the adapter

    [Fact]
    public void The_adapter_is_internal_and_sealed_does_no_io_and_reads_no_clock()
    {
        var adapter = Infrastructure.GetType("XDrishti.Infrastructure.Research.SimulationEngineAdapter");

        adapter.ShouldNotBeNull();
        adapter.IsPublic.ShouldBeFalse();
        adapter.IsSealed.ShouldBeTrue();
        adapter.GetInterfaces().ShouldContain(typeof(XDrishti.Application.Abstractions.Research.ISimulationEngine));
        adapter.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single().GetParameters().ShouldBeEmpty("stateless: nothing is injected, least of all a scoped service");
        // the namespace now also holds the database store of the charge sheet (P3-03f), which is I/O by design; the rule stays for everything else in it (CostSheetArchitectureTests pins the store itself); the result store of P3-10 is the same, pinned by ResultStoreArchitectureTests
        ShouldHaveNone(Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Research").And().DoNotHaveName("CostSheetStore").And().DoNotHaveName("RunStore").And().DoNotHaveName("ExitSelectionStore").And().DoNotHaveName("StrategyStore").And().DoNotHaveName("StrategySeeder").And().DoNotHaveName("ReportTradeSource").And().DoNotHaveName("TradeColumns").And().DoNotHaveName("TradeColumn").ShouldNot().HaveDependencyOnAny(IoAndClock).GetResult());
        ClockAndAmbientUses(Infrastructure, adapter.FullName!).ShouldBeEmpty();
        AssemblyScan.MembersUsedBy(Infrastructure, adapter.FullName!).ShouldNotContain(c => c.Type == "System.TimeProvider");
        adapter.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).ShouldBeEmpty("no state");
    }

    [Fact]
    public void The_adapter_names_no_type_of_the_strategies_library_but_the_located_problem_and_no_indicator()
    {
        ShouldHaveNone(Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Research")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Strategies.Model", "XDrishti.Strategies.BuiltIn", "XDrishti.Indicators").GetResult());
        Types.InAssembly(Infrastructure).That().HaveName("SimulationEngineAdapter").Should().HaveDependencyOn("XDrishti.Strategies.Compilation.LocatedProblem").GetResult().IsSuccessful.ShouldBeTrue("the scan sees the one use");
    }

    [Fact]
    public void The_factory_of_the_built_in_strategies_stays_in_the_library_beside_the_replay_and_the_adapter_does_no_more_than_call_it()
    {
        Simulation.GetType("XDrishti.Simulation.Replay.BuiltInStrategies").ShouldNotBeNull();
        Infrastructure.GetTypes().ShouldNotContain(t => t.Name == "BuiltInStrategies");
    }

    [Fact]
    public void The_adapter_rules_do_catch_what_they_forbid()
    {
        // the replay namespace of the library names the model and the strategies library, and the sentinel reads a file and the clock: the same rules fail on them
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").ShouldNot().HaveDependencyOnAny("XDrishti.Strategies.Model").GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(typeof(EngineUserSentinel).Assembly).That().HaveName(nameof(IoUserSentinel)).ShouldNot().HaveDependencyOnAny(IoAndClock).GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_infrastructure_registration_of_the_engine_is_a_singleton_in_the_composition_root()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "DependencyInjection.cs"));

        source.ShouldContain("AddSingleton<ISimulationEngine, SimulationEngineAdapter>()");
    }
}

/// <summary>Names a type of the simulation library on purpose, to prove the rules that keep Application away from the engine modules see such a use (it is never used by anything else).</summary>
internal static class EngineUserSentinel
{
    public static int Count() => XDrishti.Simulation.Replay.BuiltInStrategies.Ids.Count;
}

/// <summary>Reads a file and the clock on purpose, to prove the rules that keep the adapter free of I/O see such a use (it is never used by anything else).</summary>
internal static class IoUserSentinel
{
    public static string Read(string path) => File.ReadAllText(path) + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
