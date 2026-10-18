using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The simulator (P3-03, ADR 0037) is a pure library: it references only Domain among the projects and no package, knows no database, framework, HTTP, file, task or clock, keeps no mutable static state and every class is
/// sealed or static. The same walker runs in the Lab and in the replay of P3-04, so none of those can be allowed to creep in. P3-03a widens the references on purpose (Indicators, Strategies) and adds the frame rules.
/// Every rule has a sentinel in this project that proves the rule can fail.
/// </summary>
public sealed class SimulationArchitectureTests
{
    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    private static readonly string[] ForbiddenNamespaces =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
        "System.Net", "System.IO", "System.Data", "System.Threading.Tasks", "System.Diagnostics",
        "System.TimeProvider", "System.Environment", "System.Random",
    ];

    /// <summary>The frame and the compiler belong to P3-03a: nothing in this slice mentions them.</summary>
    private static readonly string[] NotYet = ["XDrishti.Indicators", "XDrishti.Strategies"];

    private static readonly string[] DocumentedNamespaces = ["XDrishti.Simulation", "XDrishti.Simulation.Walking"];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_library_references_only_domain_among_the_projects_and_no_package()
    {
        UnexpectedReferences(Simulation).ShouldBeEmpty();
    }

    [Fact]
    public void The_reference_rule_does_catch_an_assembly_that_references_other_projects_and_packages()
    {
        // This test project references Api, Worker, Feed, Indicators, Strategies and NetArchTest: the rule must see all of them.
        var violations = UnexpectedReferences(typeof(ClockSentinel).Assembly);

        violations.ShouldContain("XDrishti.Api");
        violations.ShouldContain("XDrishti.Indicators");
        violations.ShouldContain(v => v.Contains("NetArchTest", StringComparison.Ordinal));
    }

    private static List<string> UnexpectedReferences(Assembly assembly)
    {
        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
        var violations = new List<string>();
        violations.AddRange(referenced.Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal) && n != "XDrishti.Domain"));
        violations.AddRange(referenced.Where(n => !n.StartsWith("System.", StringComparison.Ordinal) && !n.StartsWith("XDrishti.", StringComparison.Ordinal) && n is not ("netstandard" or "mscorlib" or "Microsoft.CSharp")));
        violations.AddRange(referenced.Where(n => n is "System.Net.Http" or "System.Net.Primitives"));
        return violations;
    }

    [Fact]
    public void The_project_file_references_only_the_domain_project_and_no_package()
    {
        ProjectFileViolations(XDocument.Load(RepositoryFile("backend", "src", "XDrishti.Simulation", "XDrishti.Simulation.csproj"))).ShouldBeEmpty("the compiled references can hide a reference the compiler never used; the project file cannot");
    }

    [Fact]
    public void The_project_file_rule_does_catch_a_project_with_a_package_or_another_project()
    {
        const string wrong = "<Project><ItemGroup><ProjectReference Include=\"..\\XDrishti.Domain\\XDrishti.Domain.csproj\" /><ProjectReference Include=\"..\\XDrishti.Indicators\\XDrishti.Indicators.csproj\" /><PackageReference Include=\"Superpower\" /><Reference Include=\"X\" /></ItemGroup></Project>";

        var violations = ProjectFileViolations(XDocument.Parse(wrong));

        violations.ShouldContain("project XDrishti.Indicators");
        violations.ShouldContain("package Superpower");
        violations.ShouldContain("reference X");
    }

    private static List<string> ProjectFileViolations(XDocument project)
    {
        var violations = new List<string>();
        violations.AddRange(ProjectReferences(project).Where(p => p != "XDrishti.Domain").Select(p => "project " + p));
        violations.AddRange(project.Descendants("PackageReference").Select(r => "package " + (string?)r.Attribute("Include")));
        violations.AddRange(project.Descendants("Reference").Select(r => "reference " + (string?)r.Attribute("Include")));
        return violations;
    }

    [Fact]
    public void The_projects_are_in_the_solution_and_the_dockerfile_copies_the_library()
    {
        var solution = File.ReadAllText(RepositoryFile("backend", "XDrishti.slnx"));
        var dockerfile = File.ReadAllText(RepositoryFile("backend", "Dockerfile"));

        solution.ShouldContain("src/XDrishti.Simulation/XDrishti.Simulation.csproj");
        solution.ShouldContain("tests/XDrishti.Simulation.Tests/XDrishti.Simulation.Tests.csproj");
        dockerfile.ShouldContain("COPY src/XDrishti.Simulation/XDrishti.Simulation.csproj src/XDrishti.Simulation/");
    }

    [Fact]
    public void The_test_project_references_only_the_library_and_the_architecture_tests_can_see_it()
    {
        var tests = XDocument.Load(RepositoryFile("backend", "tests", "XDrishti.Simulation.Tests", "XDrishti.Simulation.Tests.csproj"));

        ProjectReferences(tests).ShouldBe(["XDrishti.Simulation"], "the walker is walked from a unit test with no database, file, YAML or market context");
        ProjectReferences(XDocument.Load(RepositoryFile("backend", "tests", "XDrishti.Architecture.Tests", "XDrishti.Architecture.Tests.csproj"))).ShouldContain("XDrishti.Simulation");
    }

    [Fact]
    public void Nothing_below_the_library_depends_on_it()
    {
        var domain = typeof(XDrishti.Domain.Common.Result).Assembly;
        var indicators = typeof(XDrishti.Indicators.IIndicator).Assembly;
        var strategies = typeof(XDrishti.Strategies.Problem).Assembly;

        domain.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Simulation");
        indicators.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Simulation");
        strategies.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Simulation");
    }

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
        var calls = SimulationScan.MemberReferences(Simulation);

        calls.Where(c => c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today").ShouldBeEmpty("DateTime.Now / UtcNow / Today");
        calls.Where(c => c.Type is "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.TimeProvider").ShouldBeEmpty("File, Directory, HttpClient, TimeProvider");
        calls.Where(c => c.Type is "System.Diagnostics.Stopwatch" or "System.Environment" or "System.Random" or "System.Threading.Tasks.Task").ShouldBeEmpty("tick counters, environment, random numbers, tasks");
    }

    [Fact]
    public void The_scan_does_find_a_clock_read_in_an_assembly()
    {
        var calls = SimulationScan.MemberReferences(typeof(ClockSentinel).Assembly);

        calls.ShouldContain(c => c.Type == "System.DateTime" && c.Member == "get_UtcNow");
        calls.ShouldContain(c => c.Type == "System.IO.File");
        calls.ShouldContain(c => c.Type == "System.Random");
    }

    [Fact]
    public void No_class_has_mutable_static_state() => SimulationScan.MutableStatics(Simulation).ShouldBeEmpty("static fields must be const or readonly");

    [Fact]
    public void The_static_state_rule_does_catch_a_violation()
    {
        SimulationScan.MutableStatics(typeof(ClockSentinel).Assembly).ShouldContain(name => name.EndsWith("ClockSentinel.Counter", StringComparison.Ordinal));
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

    [Fact]
    public void Public_types_are_only_in_the_documented_namespaces()
    {
        Simulation.GetExportedTypes().Select(t => t.Namespace).Distinct().ShouldBeSubsetOf(DocumentedNamespaces);
        Simulation.GetExportedTypes().Select(t => t.Namespace).Distinct().Count().ShouldBe(DocumentedNamespaces.Length, "both namespaces hold public types");
    }

    [Fact]
    public void The_walking_namespace_depends_on_the_root_and_the_root_on_nothing_in_walking()
    {
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespaceMatching(@"^XDrishti\.Simulation$").ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking").GetResult());
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Walking").ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces).GetResult());
    }

    [Fact]
    public void The_root_namespace_rule_does_catch_a_type_that_uses_the_walker()
    {
        // WalkerUserSentinel (this test project) calls TradeWalker: the dependency the root of the library must never have.
        Types.InAssembly(typeof(WalkerUserSentinel).Assembly).That().HaveName(nameof(WalkerUserSentinel)).ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_in_the_library_mentions_the_indicators_the_strategies_or_the_feature_frame()
    {
        ShouldHaveNone(Types.InAssembly(Simulation).That().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOnAny(NotYet).GetResult());
        SimulationScan.MemberReferences(Simulation).Where(c => c.Type.EndsWith("FeatureFrame", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_frame_rule_does_catch_a_type_that_uses_the_frame()
    {
        Types.InAssembly(typeof(FrameFeedSentinel).Assembly).That().HaveName(nameof(FrameFeedSentinel)).ShouldNot().HaveDependencyOnAny(NotYet).GetResult().IsSuccessful.ShouldBeFalse();
        SimulationScan.MemberReferences(typeof(FrameFeedSentinel).Assembly).ShouldContain(c => c.Type == "XDrishti.Indicators.FeatureFrame");
    }

    private static List<string> ProjectReferences(XDocument project) =>
        [.. project.Descendants("ProjectReference").Select(r => Path.GetFileNameWithoutExtension(((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/')))];

    /// <summary>Finds a file of the repository by walking up from the test binaries (the tests run from a checkout, never from a published image).</summary>
    private static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The file " + string.Join('/', parts) + " was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Reads the member and type references recorded in an assembly's metadata, and finds static fields that can change.</summary>
    private static class SimulationScan
    {
        public static List<(string Type, string Member)> MemberReferences(Assembly assembly)
        {
            using var stream = File.OpenRead(assembly.Location);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var found = new List<(string Type, string Member)>();
            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                if (member.Parent.Kind == HandleKind.TypeReference)
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    found.Add((reader.GetString(type.Namespace) + "." + reader.GetString(type.Name), reader.GetString(member.Name)));
                }
            }

            foreach (var handle in reader.TypeReferences)
            {
                var type = reader.GetTypeReference(handle);
                found.Add((reader.GetString(type.Namespace) + "." + reader.GetString(type.Name), string.Empty));
            }

            return found;
        }

        public static List<string> MutableStatics(Assembly assembly)
        {
            var offenders = new List<string>();
            foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null))
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsInitOnly && !field.IsLiteral)
                    {
                        offenders.Add(type.FullName + "." + field.Name);
                    }
                }
            }

            return offenders;
        }
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

/// <summary>Calls the trade walker on purpose, to prove the rule that the root namespace of the library never depends on <c>Walking</c> sees such a dependency (it is never used by anything else).</summary>
internal static class WalkerUserSentinel
{
    public static object Use(XDrishti.Simulation.EntryCandidate candidate) =>
        XDrishti.Simulation.Walking.TradeWalker.Walk(candidate, [], new XDrishti.Simulation.Walking.WalkRules(null, 915));
}
