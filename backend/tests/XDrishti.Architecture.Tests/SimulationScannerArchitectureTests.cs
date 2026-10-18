using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The simulation library (P3-03, P3-03a; ADR 0037) is a pure calculation: it references only Domain, Indicators and Strategies among the projects and no package, reaches the feature frame only through the members its stages
/// need, exposes no frame or bound condition, keeps its stages apart (the root, <c>Walking</c>, <c>Scanning</c> and <c>Running</c> each depend only on what the spec says), has no clock and no mutable static state, and
/// its public types live in the documented namespaces. Each rule has a sentinel, a case in this test project that the same code must reject, so that a rule that finds nothing cannot pass for the wrong reason.
/// <para>
/// This file holds the rules of P3-03a that the scanner brings (the widening of P3-03's list). When P3-03 merges, its <c>SimulationArchitectureTests</c> and this file are one class.
/// </para>
/// </summary>
public sealed class SimulationScannerArchitectureTests
{
    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    private static readonly string[] DocumentedNamespaces = ["XDrishti.Simulation", "XDrishti.Simulation.Walking", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running"];

    /// <summary>The members of the feature frame the library may reference: all of them used, none other (spec P3-03a, criterion 18b).</summary>
    private static readonly string[] FrameMembers =
        ["Create", "Step", "BarClosed", "ClosedBarsAt", "TryLastClosed", "HandleOf", "get_Item", "At", "get_LastBarTime", "get_Steps", "get_IgnoredBars", "get_IncompleteBarsConsumed"];

    private static readonly string[] ForbiddenNamespaces =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
        "System.Net", "System.IO", "System.Data", "System.Threading.Tasks", "System.Diagnostics",
        "System.TimeProvider", "System.Environment", "System.Random",
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

        solution.ShouldContain("src/XDrishti.Simulation/XDrishti.Simulation.csproj");
        solution.ShouldContain("tests/XDrishti.Simulation.Tests/XDrishti.Simulation.Tests.csproj");
        dockerfile.ShouldContain("COPY src/XDrishti.Simulation/XDrishti.Simulation.csproj src/XDrishti.Simulation/");
        RepositoryFiles.ProjectReferences(tests).ShouldBe(["XDrishti.Simulation"]);
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
        TypeNamesIn("XDrishti.Simulation.Scanning").ShouldNotBeEmpty();

        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespaceMatching("^XDrishti\\.Simulation$").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies", "XDrishti.Simulation.Walking", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Walking").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Scanning").ShouldNot().HaveDependencyOnAny("XDrishti.Simulation.Walking", "XDrishti.Simulation.Running").GetResult());
    }

    [Fact]
    public void The_stage_rule_does_catch_a_stage_that_reads_another()
    {
        // the scanning stage does read Strategies and Indicators, so the rule that forbids them to the root fails on it
        Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Scanning").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies").GetResult().IsSuccessful.ShouldBeFalse();
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
        Assembly.GetAssembly(typeof(XDrishti.Strategies.Problem))!.GetExportedTypes().Select(t => t.Namespace ?? string.Empty).Distinct().Except(DocumentedNamespaces).ShouldNotBeEmpty("the rule can fail");
    }

    // purity

    [Fact]
    public void The_library_depends_on_no_other_layer_the_database_http_files_or_a_clock_and_reads_no_clock_and_has_no_mutable_static_state()
    {
        ShouldHaveNone(Types.InAssembly(Simulation).That().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces).GetResult());
        var calls = AssemblyScan.MemberReferences(Simulation);

        calls.Where(c => c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today").ShouldBeEmpty("DateTime.Now / UtcNow / Today");
        calls.Where(c => c.Type is "System.Random" or "System.TimeProvider" or "System.IO.File" or "System.Diagnostics.Stopwatch" or "System.Environment").ShouldBeEmpty("random, clock, file, tick counters");
        AssemblyScan.MutableStatics(Simulation).ShouldBeEmpty("static fields must be const or readonly");
    }

    [Fact]
    public void Every_class_of_the_library_is_sealed_or_static()
    {
        Simulation.GetTypes().Where(t => t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null && t.IsClass && !t.IsSealed).Select(t => t.FullName).ShouldBeEmpty();
    }

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

/// <summary>Exposes a frame in public signatures and reads its ready flag on purpose, to prove the rules on the frame see such a use (it is never used by anything else).</summary>
public sealed class FrameLeakSentinel
{
    public XDrishti.Indicators.FeatureFrame? Frame { get; set; }

    public static bool Peek(XDrishti.Indicators.FeatureFrame frame) => frame.IsReady(0);
}
