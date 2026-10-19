using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The simulation library (P3-03, P3-03a; ADR 0037) is a pure calculation: it references only Domain, Indicators and Strategies among the projects and no package, reaches the feature frame only through the members its stages
/// need, exposes no frame or bound condition, keeps its stages apart (the root, <c>Walking</c>, <c>Scanning</c> and <c>Running</c> each depend only on what the spec says), knows no database, framework, HTTP, file, task or clock,
/// keeps no mutable static state, every class is sealed or static, and its public types live in the documented namespaces. The same walker runs in the Lab and in the replay of P3-04, so none of those can be allowed to creep in.
/// Each rule has a sentinel, a case in this test project that the same code must reject, so that a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed class SimulationArchitectureTests
{
    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    private static readonly string[] DocumentedNamespaces = ["XDrishti.Simulation", "XDrishti.Simulation.Walking", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running", "XDrishti.Simulation.Replay"];

    /// <summary>The members of the feature frame the library may reference: all of them used, none other (spec P3-03a, criterion 18b).</summary>
    private static readonly string[] FrameMembers =
        ["Create", "Step", "BarClosed", "ClosedBarsAt", "TryLastClosed", "HandleOf", "get_Item", "At", "get_LastBarTime", "get_Steps", "get_IgnoredBars", "get_IncompleteBarsConsumed"];

    private static readonly string[] ForbiddenNamespaces =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
        "System.Net", "System.IO", "System.Data", "System.Threading.Tasks", "System.Diagnostics",
        "System.TimeProvider", "System.Environment", "System.Random",
        "System.Text.Json", // the canonical JSON of the replay is hand-written: the bytes are ours and not a framework version's (spec P3-04, Scope 2)
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    // (a) references

    private static List<string> ProjectsReferencedBy(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal))];

    private static List<string> PackagesReferencedBy(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)
            .Where(n => !n.StartsWith("System.", StringComparison.Ordinal) && !n.StartsWith("XDrishti.", StringComparison.Ordinal) && n is not ("netstandard" or "mscorlib" or "Microsoft.CSharp"))];

    [Fact]
    public void The_library_references_only_domain_indicators_and_strategies_among_the_projects_and_no_package()
    {
        ProjectsReferencedBy(Simulation).ShouldBeSubsetOf(["XDrishti.Domain", "XDrishti.Indicators", "XDrishti.Strategies"]);
        PackagesReferencedBy(Simulation).ShouldBeEmpty();
        Simulation.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(n => n != null && n.StartsWith("Microsoft.", StringComparison.Ordinal) && n != "Microsoft.CSharp");

        var project = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Simulation", "XDrishti.Simulation.csproj"));
        RepositoryFiles.ProjectReferences(project).ShouldBe(["XDrishti.Domain", "XDrishti.Indicators", "XDrishti.Strategies"], "the compiled references can hide a reference the compiler never used; the project file cannot");
        project.Descendants("PackageReference").ShouldBeEmpty();
        project.Descendants("Reference").ShouldBeEmpty();
    }

    [Fact]
    public void The_reference_rule_does_see_a_package_and_a_project_the_library_must_not_have()
    {
        PackagesReferencedBy(typeof(XDrishti.Strategies.Problem).Assembly).ShouldContain("Superpower");
        ProjectsReferencedBy(typeof(XDrishti.Strategies.Problem).Assembly).Except(["XDrishti.Domain"]).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_projects_are_in_the_solution_the_dockerfile_copies_the_library_and_the_test_project_references_only_it()
    {
        var solution = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"));
        var dockerfile = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "Dockerfile"));
        var tests = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "tests", "XDrishti.Simulation.Tests", "XDrishti.Simulation.Tests.csproj"));
        var architecture = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "tests", "XDrishti.Architecture.Tests", "XDrishti.Architecture.Tests.csproj"));

        solution.ShouldContain("src/XDrishti.Simulation/XDrishti.Simulation.csproj");
        solution.ShouldContain("tests/XDrishti.Simulation.Tests/XDrishti.Simulation.Tests.csproj");
        dockerfile.ShouldContain("COPY src/XDrishti.Simulation/XDrishti.Simulation.csproj src/XDrishti.Simulation/");
        RepositoryFiles.ProjectReferences(tests).Order(StringComparer.Ordinal).ToList().ShouldBe(["XDrishti.SimOracle", "XDrishti.Simulation"], "the walker is walked from a unit test with no database, file, YAML or market context; the independent oracle (ADR 0047) is the second reference");
        RepositoryFiles.ProjectReferences(architecture).ShouldContain("XDrishti.Simulation");
    }

    [Fact]
    public void Nothing_below_the_library_depends_on_it()
    {
        foreach (var below in new[] { typeof(XDrishti.Domain.Common.Result).Assembly, typeof(XDrishti.Indicators.IIndicator).Assembly, typeof(XDrishti.Strategies.Problem).Assembly })
        {
            below.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Simulation", below.GetName().Name);
        }
    }

    // (b) the members of the frame

    [Fact]
    public void The_library_reaches_the_feature_frame_only_through_the_members_it_needs_and_uses_all_of_them()
    {
        var used = AssemblyScan.FrameMembersUsed(Simulation);

        used.Except(FrameMembers).ShouldBeEmpty("a member of the frame that is not on the list of the spec");
        FrameMembers.Except(used).ShouldBeEmpty("the scan sees every member the library uses; a rule that finds nothing would pass for the wrong reason, and a member nobody uses should leave the list");
    }

    [Fact]
    public void The_frame_member_rule_does_catch_a_library_that_reads_the_ready_flag_or_resets_the_frame()
    {
        var used = AssemblyScan.FrameMembersUsed(typeof(FrameLeakSentinel).Assembly);

        used.ShouldContain("IsReady");
        used.Except(FrameMembers).ShouldNotBeEmpty();
    }

    // (c) the stages

    private static List<string> TypeNamesIn(string @namespace) => [.. Simulation.GetTypes().Where(t => t.Namespace == @namespace).Select(t => t.FullName ?? t.Name)];

    [Fact]
    public void The_root_depends_on_no_stage_and_on_nothing_in_indicators_or_strategies_and_the_scanning_stage_on_no_other_stage()
    {
        TypeNamesIn("XDrishti.Simulation").ShouldNotBeEmpty();
        TypeNamesIn("XDrishti.Simulation.Walking").ShouldNotBeEmpty();
        TypeNamesIn("XDrishti.Simulation.Scanning").ShouldNotBeEmpty();
        TypeNamesIn("XDrishti.Simulation.Running").ShouldNotBeEmpty();
        TypeNamesIn("XDrishti.Simulation.Replay").ShouldNotBeEmpty();

        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespaceMatching("^XDrishti\\.Simulation$").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies", "XDrishti.Simulation.Walking", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running", "XDrishti.Simulation.Replay").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Walking").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running", "XDrishti.Simulation.Replay").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Scanning").ShouldNot().HaveDependencyOnAny("XDrishti.Simulation.Walking", "XDrishti.Simulation.Running", "XDrishti.Simulation.Replay").GetResult());
    }

    private static readonly string[] RunningTypes = ["Simulator", "SimulatorBuild", "SimulationResult", "SimulationHeader", "CandidateOutcome", "SimulatorCodes"];

    [Fact]
    public void The_running_stage_may_use_the_walker_and_the_scanner_and_holds_the_result_types_no_stage_below_it_could_name()
    {
        // Running composes the two stages (and so depends on both); the rules above forbid the reverse. The result types hold a type of Walking, so the root namespace cannot be their home (spec P3-03a, scope 2).
        ShouldHaveNone(Types.InAssembly(Simulation).That().HaveName("Simulator").Should().HaveDependencyOnAll("XDrishti.Simulation.Walking", "XDrishti.Simulation.Scanning").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().HaveName(nameof(XDrishti.Simulation.Running.CandidateOutcome)).Should().HaveDependencyOn("XDrishti.Simulation.Walking").GetResult());
        foreach (var name in RunningTypes)
        {
            Simulation.GetTypes().Where(t => t.Name == name).Select(t => t.Namespace).ShouldBe(["XDrishti.Simulation.Running"], name);
        }

        Simulation.GetTypes().Where(t => t.Namespace != "XDrishti.Simulation.Running").Select(t => t.Name).Intersect(RunningTypes).ShouldBeEmpty();
    }

    [Fact]
    public void The_running_stage_does_not_reach_the_frame_the_indicators_or_the_bound_side()
    {
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Running").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Simulation.Replay").GetResult());
    }

    [Fact]
    public void The_replay_namespace_uses_the_running_stage_and_the_strategies_library_but_not_the_walker_the_frame_or_the_indicators()
    {
        // spec P3-04, criterion 15: the replay calls Simulator.Run once per segment; it reads no frame and no indicator (it reads the WalkOutcome that Running hands it, but never calls the walker)
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking.TradeWalker").GetResult());
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Running").ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking.TradeWalker").GetResult().IsSuccessful.ShouldBeFalse("the rule sees a call of the walker where there is one");
        // ... and the rules above can fail: the same namespace does depend on the running stage and on the strategies library
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").ShouldNot().HaveDependencyOnAny("XDrishti.Simulation.Running").GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").ShouldNot().HaveDependencyOnAny("XDrishti.Strategies").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_replay_namespace_holds_the_documented_types_and_the_canonical_json_writer_is_internal()
    {
        TypeNamesIn("XDrishti.Simulation.Replay").Where(n => !n.Contains('+', StringComparison.Ordinal) && !n.Contains('<', StringComparison.Ordinal)).Select(n => n["XDrishti.Simulation.Replay.".Length..]).Order().ShouldBe(
            ["BarSegment", "BuiltInBuild", "BuiltInEntry", "BuiltInStrategies", "DefinitionJson", "Discontinuity", "InstrumentReplayer", "PreparedBuild", "PreparedStrategy", "RanSession", "ReplayBars", "SessionScan", "StrategyPreparation"]);
        Simulation.GetType("XDrishti.Simulation.Replay.DefinitionJson")!.IsPublic.ShouldBeFalse();
        Simulation.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Simulation.Replay").Select(t => t.Name).Order().ShouldBe(
            ["BuiltInBuild", "BuiltInStrategies", "InstrumentReplayer", "PreparedBuild", "PreparedStrategy", "StrategyPreparation"]);
    }

    [Fact]
    public void The_json_rule_does_catch_a_type_that_uses_the_json_serializer()
    {
        Violates(nameof(JsonSentinel), "System.Text.Json").ShouldBeTrue();
    }

    [Fact]
    public void The_running_rules_do_catch_a_stage_that_reads_another_or_the_indicators()
    {
        // Running does use Walking and Scanning, and the scanning stage does use Indicators: the rules that forbid those dependencies to other places fail on them
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Running").ShouldNot().HaveDependencyOnAny("XDrishti.Simulation.Walking").GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Scanning").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_tick_grid_is_internal_to_the_walking_stage_and_the_root_and_the_domain_know_nothing_of_it()
    {
        // P3-03b criterion 23: the grid arithmetic is a detail of Walking (decimal, no public surface); the value types the stage shares with the run record are in Domain.Research.
        var grid = Simulation.GetType("XDrishti.Simulation.Walking.TickGrid");

        grid.ShouldNotBeNull();
        grid.IsPublic.ShouldBeFalse();
        grid.IsAbstract.ShouldBeTrue("a static class");
        grid.IsSealed.ShouldBeTrue("a static class");
        Simulation.GetExportedTypes().ShouldNotContain(grid);
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespaceMatching("^XDrishti\\.Simulation$").ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking.TickGrid").GetResult());
        typeof(XDrishti.Domain.Research.TickSpec).Assembly.GetTypes().ShouldNotContain(t => t.Name == "TickGrid");
    }

    [Fact]
    public void The_stage_rule_does_catch_a_stage_that_reads_another()
    {
        // the scanning stage does read Strategies and Indicators, so the rule that forbids them to the root fails on it
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Scanning").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_root_namespace_rule_does_catch_a_type_that_uses_the_walker()
    {
        // WalkerUserSentinel (this test project) calls TradeWalker: the dependency the root of the library must never have.
        Types.InAssembly(typeof(WalkerUserSentinel).Assembly).That().HaveName(nameof(WalkerUserSentinel)).ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking").GetResult().IsSuccessful.ShouldBeFalse();
    }

    // (d) no signature exposes the frame

    private static readonly string[] Hidden = ["XDrishti.Indicators.FeatureFrame", "XDrishti.Strategies.Compilation.BoundSide", "XDrishti.Strategies.Compilation.BoundCondition"];

    private static List<string> Exposures(Assembly assembly)
    {
        var found = new List<string>();
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var visible = member switch
                {
                    MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
                    FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
                    PropertyInfo property => (property.GetMethod?.IsPublic ?? false) || (property.GetMethod?.IsFamily ?? false),
                    _ => false,
                };
                if (!visible)
                {
                    continue;
                }

                var types = member switch
                {
                    MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
                    ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
                    FieldInfo field => [field.FieldType],
                    PropertyInfo property => [property.PropertyType],
                    _ => [],
                };
                found.AddRange(types.SelectMany(Flatten).Where(t => Hidden.Contains(t.FullName)).Select(t => type.FullName + "." + member.Name + " exposes " + t.Name));
            }
        }

        return found;
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return inner;
            }
        }
    }

    [Fact]
    public void No_public_signature_of_the_library_exposes_the_frame_a_bound_side_or_a_bound_condition()
    {
        Exposures(Simulation).ShouldBeEmpty();
    }

    [Fact]
    public void The_exposure_rule_does_catch_a_signature_that_takes_or_returns_a_frame()
    {
        var found = Exposures(typeof(FrameLeakSentinel).Assembly);

        found.ShouldContain(e => e.StartsWith(typeof(FrameLeakSentinel).FullName + ".Peek exposes FeatureFrame", StringComparison.Ordinal));
        found.ShouldContain(e => e.Contains("Frame exposes FeatureFrame", StringComparison.Ordinal));
    }

    // (e) the namespaces

    [Fact]
    public void Public_types_are_in_the_documented_namespaces()
    {
        var namespaces = Simulation.GetExportedTypes().Select(t => t.Namespace ?? string.Empty).Distinct().ToList();

        namespaces.ShouldBeSubsetOf(DocumentedNamespaces);
        namespaces.ShouldContain("XDrishti.Simulation.Scanning");
        namespaces.ShouldContain("XDrishti.Simulation.Running");
        namespaces.ShouldContain("XDrishti.Simulation.Walking");
        namespaces.ShouldContain("XDrishti.Simulation.Replay");
        Assembly.GetAssembly(typeof(XDrishti.Strategies.Problem))!.GetExportedTypes().Select(t => t.Namespace ?? string.Empty).Distinct().Except(DocumentedNamespaces).ShouldNotBeEmpty("the rule can fail");
    }

    // (f) purity

    [Fact]
    public void No_type_depends_on_other_layers_the_database_http_files_tasks_or_a_clock()
    {
        ShouldHaveNone(Types.InAssembly(Simulation).That().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces).GetResult());
    }

    [Fact]
    public void The_dependency_rule_does_catch_a_file_a_task_a_random_number_and_the_environment()
    {
        // ClockSentinel reads a file; AmbientSentinel starts a task, draws a random number and reads the environment (both in this test project, never used by anything else).
        Violates(nameof(ClockSentinel), "System.IO").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.Threading.Tasks").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.Random").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.Environment").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.TimeProvider").ShouldBeTrue();
    }

    private static bool Violates(string typeName, string ns) =>
        !Types.InAssembly(typeof(ClockSentinel).Assembly).That().HaveName(typeName).ShouldNot().HaveDependencyOnAny(ns).GetResult().IsSuccessful;

    [Fact]
    public void Nothing_reads_the_clock_or_touches_files_or_the_network_or_ambient_state()
    {
        var calls = AssemblyScan.MemberReferences(Simulation);

        calls.Where(c => c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today").ShouldBeEmpty("DateTime.Now / UtcNow / Today");
        calls.Where(c => c.Type is "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.TimeProvider").ShouldBeEmpty("File, Directory, HttpClient, TimeProvider");
        calls.Where(c => c.Type is "System.Diagnostics.Stopwatch" or "System.Environment" or "System.Random" or "System.Threading.Tasks.Task").ShouldBeEmpty("tick counters, environment, random numbers, tasks");
    }

    [Fact]
    public void The_scan_does_find_a_clock_read_in_an_assembly()
    {
        var calls = AssemblyScan.MemberReferences(typeof(ClockSentinel).Assembly);

        calls.ShouldContain(c => c.Type == "System.DateTime" && c.Member == "get_UtcNow");
        calls.ShouldContain(c => c.Type == "System.IO.File");
        calls.ShouldContain(c => c.Type == "System.Random");
    }

    [Fact]
    public void No_class_has_mutable_static_state() => AssemblyScan.MutableStatics(Simulation).ShouldBeEmpty("static fields must be const or readonly");

    [Fact]
    public void The_static_state_rule_does_catch_a_violation()
    {
        AssemblyScan.MutableStatics(typeof(ClockSentinel).Assembly).ShouldContain(name => name.EndsWith("ClockSentinel.Counter", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_class_is_sealed_or_static()
    {
        Unsealed(Simulation).ShouldBeEmpty("a class designed for inheritance needs a documented reason");
    }

    [Fact]
    public void The_sealed_rule_does_catch_a_class_that_can_be_inherited()
    {
        Unsealed(typeof(ClockSentinel).Assembly).ShouldContain(typeof(UnsealedSentinel).FullName);
    }

    private static List<string?> Unsealed(Assembly assembly) =>
        [.. assembly.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null && t.IsClass && !t.IsSealed).Select(t => t.FullName)];

    // (g) the compiler warm-up

    [Fact]
    public void The_compiler_warm_up_rule_is_met_non_vacuously_by_the_simulation_library_and_the_api_host()
    {
        var sourceRoot = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "src");
        var users = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => Path.GetFileNameWithoutExtension(file) != "XDrishti.Strategies" && RepositoryFiles.ProjectReferences(XDocument.Load(file)).Contains("XDrishti.Strategies"))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();
        var callers = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !RepositoryFiles.IsBuildOutput(file) && !RepositoryFiles.IsUnder(file, Path.Combine(sourceRoot, "XDrishti.Strategies")) && RepositoryFiles.CallsTheWarmUp(File.ReadAllText(file)))
            .ToList();
        var setup = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Infrastructure", "ApiSetup.cs"));

        users.ShouldContain("XDrishti.Simulation");
        users.ShouldContain("XDrishti.Api");
        callers.ShouldNotBeEmpty();
        setup.ShouldContain("AddHostedService(sp => new StrategyWarmupService(");
        setup.ShouldContain("CompilerWarmup.Run");
    }
}

/// <summary>A class that can be inherited, on purpose, to prove the sealed rule sees it (it is never used by anything else).</summary>
public class UnsealedSentinel
{
}

/// <summary>Starts a task, draws a random number, reads the environment and takes a time provider on purpose, to prove the ambient-state rules see them (it is never used by anything else).</summary>
internal static class AmbientSentinel
{
    public static Task Start() => Task.Run(() => Environment.GetEnvironmentVariable("X"));

    public static int Draw() => new Random().Next();

    public static long Now(TimeProvider time) => time.GetUtcNow().ToUnixTimeSeconds();
}

/// <summary>Serializes with <c>System.Text.Json</c> on purpose, to prove the rule that the library writes its canonical text by hand sees such a use (it is never used by anything else).</summary>
internal static class JsonSentinel
{
    public static string Write(int value) => System.Text.Json.JsonSerializer.Serialize(value);
}

/// <summary>Calls the trade walker on purpose, to prove the rule that the root namespace of the library never depends on <c>Walking</c> sees such a dependency (it is never used by anything else).</summary>
internal static class WalkerUserSentinel
{
    public static object Use(XDrishti.Simulation.EntryCandidate candidate) =>
        XDrishti.Simulation.Walking.TradeWalker.Walk(candidate, [], new XDrishti.Simulation.Walking.WalkRules(null, 915, XDrishti.Domain.Research.FillRules.Default, XDrishti.Domain.Research.TickSpec.FromRupees(0.05m).Value));
}

/// <summary>Exposes a frame in public signatures and reads its ready flag on purpose, to prove the rules on the frame see such a use (it is never used by anything else).</summary>
public sealed class FrameLeakSentinel
{
    public XDrishti.Indicators.FeatureFrame? Frame { get; set; }

    public static bool Peek(XDrishti.Indicators.FeatureFrame frame) => frame.IsReady(0);
}
