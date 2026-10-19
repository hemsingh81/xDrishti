using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The export library (P3-04b1; the ADR comes with P3-04b2) is pure: it references only Domain, does no I/O (it writes UTF-8 to an <c>IBufferWriter</c> that the host owns), reads no clock, no random source, no machine fact and no environment,
/// builds no copy of a whole file, keeps no mutable static state, and every class is sealed or static. Only <c>XDrishti.Api</c> references it (its folder writer is the one place that touches the file system for the export), and Application
/// never does (ADR 0039: a renderer is not a port). Each rule has a sentinel, a case in this test project that the same code must reject, so that a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed class SimExportArchitectureTests
{
    private static readonly Assembly SimExport = typeof(XDrishti.SimExport.ExportSpellings).Assembly;

    private static readonly string[] DocumentedNamespaces = ["XDrishti.SimExport", "XDrishti.SimExport.Cards", "XDrishti.SimExport.Csv", "XDrishti.SimExport.Json", "XDrishti.SimExport.Manifest", "XDrishti.SimExport.Text", "XDrishti.SimExport.Verification"];

    private static readonly string[] ForbiddenNamespaces =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed", "XDrishti.Simulation", "XDrishti.Indicators", "XDrishti.Strategies",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
        "System.Net", "System.IO", "System.Data", "System.Diagnostics", "System.Console", "System.Environment", "System.Random", "System.TimeProvider", "System.TimeZoneInfo", "System.Threading.Tasks",
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static string SourceRoot() => Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "src");

    private static List<string> SourceProjectsReferencing(string project) =>
    [
        .. Directory.GetFiles(SourceRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(file => !RepositoryFiles.IsBuildOutput(file) && RepositoryFiles.ProjectReferences(XDocument.Load(file)).Contains(project))
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Order(StringComparer.Ordinal),
    ];

    // (a) references

    [Fact]
    public void The_library_references_only_the_domain_among_the_projects_and_no_package_in_the_project_file_and_in_the_compiled_assembly()
    {
        var project = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.SimExport", "XDrishti.SimExport.csproj"));
        var referenced = SimExport.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        RepositoryFiles.ProjectReferences(project).ShouldBe(["XDrishti.Domain"], "the compiled references can hide a reference the compiler never used; the project file cannot");
        project.Descendants("PackageReference").ShouldBeEmpty();
        project.Descendants("Reference").ShouldBeEmpty();
        referenced.Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).Distinct().ShouldBe(["XDrishti.Domain"]);
        referenced.ShouldNotContain(n => n.StartsWith("Microsoft.", StringComparison.Ordinal) && n != "Microsoft.CSharp");
        referenced.ShouldNotContain(n => n.StartsWith("Npgsql", StringComparison.Ordinal) || n.StartsWith("Serilog", StringComparison.Ordinal));
        referenced.ShouldNotContain("System.Net.Http");
    }

    [Fact]
    public void Nothing_in_src_but_the_api_references_the_library_and_the_simulation_library_is_still_for_infrastructure_only()
    {
        SourceProjectsReferencing("XDrishti.SimExport").ShouldBe(["XDrishti.Api"]);
        SourceProjectsReferencing("XDrishti.Simulation").ShouldBe(["XDrishti.Infrastructure"]);
        SourceProjectsReferencing("XDrishti.Domain").ShouldContain("XDrishti.SimExport", "the scan does see a reference");
    }

    [Fact]
    public void Nothing_below_or_beside_the_api_depends_on_the_library()
    {
        foreach (var other in new[] { typeof(XDrishti.Domain.Common.Result).Assembly, typeof(XDrishti.Application.DependencyInjection).Assembly, typeof(XDrishti.Infrastructure.DependencyInjection).Assembly, typeof(XDrishti.Simulation.SimulationEngine).Assembly })
        {
            other.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.SimExport", other.GetName().Name);
        }

        ShouldHaveNone(Types.InAssembly(typeof(XDrishti.Application.DependencyInjection).Assembly).ShouldNot().HaveDependencyOn("XDrishti.SimExport").GetResult());
    }

    [Fact]
    public void The_projects_are_in_the_solution_and_the_dockerfile_copies_the_library_and_the_test_project_references_only_it()
    {
        var solution = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"));
        var dockerfile = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "Dockerfile"));
        var tests = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "tests", "XDrishti.SimExport.Tests", "XDrishti.SimExport.Tests.csproj"));

        solution.ShouldContain("src/XDrishti.SimExport/XDrishti.SimExport.csproj");
        solution.ShouldContain("tests/XDrishti.SimExport.Tests/XDrishti.SimExport.Tests.csproj");
        dockerfile.ShouldContain("COPY src/XDrishti.SimExport/XDrishti.SimExport.csproj src/XDrishti.SimExport/");
        RepositoryFiles.ProjectReferences(tests).ShouldBe(["XDrishti.SimExport"], "the renderers are tested without a database, a file or an engine");
    }

    // (b) purity

    [Fact]
    public void No_type_depends_on_other_layers_the_database_http_files_the_console_the_environment_or_a_clock()
    {
        ShouldHaveNone(Types.InAssembly(SimExport).That().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces).GetResult());
    }

    [Fact]
    public void The_dependency_rule_does_catch_a_file_the_console_a_task_a_random_number_and_the_environment()
    {
        Violates(nameof(ClockSentinel), "System.IO").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.Threading.Tasks").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.Random").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.Environment").ShouldBeTrue();
        Violates(nameof(AmbientSentinel), "System.TimeProvider").ShouldBeTrue();
        Violates(nameof(ExportSentinel), "System.Console").ShouldBeTrue();
        Violates(nameof(ExportSentinel), "System.TimeZoneInfo").ShouldBeTrue();
        Violates(nameof(ExportSentinel), "System.Diagnostics").ShouldBeTrue();
    }

    private static bool Violates(string typeName, string ns) =>
        !Types.InAssembly(typeof(ClockSentinel).Assembly).That().HaveName(typeName).ShouldNot().HaveDependencyOnAny(ns).GetResult().IsSuccessful;

    [Fact]
    public void Nothing_reads_the_clock_the_zone_a_new_guid_a_random_number_the_environment_a_file_or_the_console()
    {
        var calls = AssemblyScan.MemberReferences(SimExport);

        calls.Where(c => c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today" or "ToLocalTime").ShouldBeEmpty("DateTime.Now / UtcNow / Today / ToLocalTime");
        calls.Where(c => c.Type is "System.TimeZoneInfo" or "System.TimeProvider" or "System.Diagnostics.Stopwatch" or "System.Diagnostics.Process").ShouldBeEmpty("zones, time providers, tick counters, processes");
        calls.Where(c => c.Type is "System.Guid" && c.Member is "NewGuid" or "CreateVersion7").ShouldBeEmpty("Guid.NewGuid");
        calls.Where(c => c.Type is "System.Random" or "System.Environment" or "System.Console" or "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.Security.Cryptography.RandomNumberGenerator").ShouldBeEmpty("random numbers, the environment, the console, files, the network");
    }

    [Fact]
    public void The_scan_does_find_the_calls_of_the_sentinels()
    {
        var calls = AssemblyScan.MemberReferences(typeof(ClockSentinel).Assembly);

        calls.ShouldContain(c => c.Type == "System.DateTime" && c.Member == "get_UtcNow");
        calls.ShouldContain(c => c.Type == "System.Guid" && c.Member == "NewGuid");
        calls.ShouldContain(c => c.Type == "System.Random");
        calls.ShouldContain(c => c.Type == "System.Environment");
        calls.ShouldContain(c => c.Type == "System.Console");
        calls.ShouldContain(c => c.Type == "System.TimeZoneInfo");
    }

    [Fact]
    public void The_verification_code_reads_no_clock_no_random_source_and_no_machine_value_in_its_source()
    {
        var folder = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.SimExport", "Verification", "VerificationSampler.cs"))!;
        var files = Directory.GetFiles(folder, "*.cs");

        files.Length.ShouldBeGreaterThan(8, "the scan does see the verification code");
        foreach (var file in files)
        {
            var code = string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            foreach (var banned in new[] { "new Random", "Random.Shared", "DateTime.Now", "UtcNow", "TimeProvider", "Environment.", "Guid.NewGuid", "Stopwatch", "File.", "Directory.", "Console." })
            {
                code.ShouldNotContain(banned, Case.Sensitive, Path.GetFileName(file));
            }
        }

        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Simulate", "ExportFolderWriter.cs")).ShouldContain("RandomNumberGenerator", Case.Sensitive, "the scan does see a random source where there is one");
    }

    [Fact]
    public void No_renderer_builds_a_copy_of_a_whole_file()
    {
        // the library writes to the sink of the host: no MemoryStream, no ArrayBufferWriter, no ToArray, no string of a file in the csv and json renderers (the manifest is a few kilobytes and returns its bytes)
        foreach (var type in SimExport.GetTypes().Where(t => t.Namespace is "XDrishti.SimExport.Csv" or "XDrishti.SimExport.Json" && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null && !t.IsNested))
        {
            var used = AssemblyScan.MembersUsedBy(SimExport, type.FullName!);

            used.ShouldNotContain(c => c.Type.StartsWith("System.IO.MemoryStream", StringComparison.Ordinal), type.Name);
            used.ShouldNotContain(c => c.Type.Contains("ArrayBufferWriter", StringComparison.Ordinal), type.Name);
            used.Any(c => c.Member is "ToArray" or "GetString").ShouldBeFalse(type.Name);
        }

        AssemblyScan.MembersUsedBy(typeof(ClockSentinel).Assembly, typeof(ExportSentinel).FullName!).ShouldContain(c => c.Member == "ToArray", "the scan does see a copy");
        AssemblyScan.MembersUsedBy(typeof(ClockSentinel).Assembly, typeof(ExportSentinel).FullName!).ShouldContain(c => c.Type.StartsWith("System.IO.MemoryStream", StringComparison.Ordinal));
    }

    [Fact]
    public void The_writers_take_a_sink_and_return_nothing_so_no_copy_of_the_output_is_handed_back()
    {
        foreach (var (type, name) in new[] { (typeof(XDrishti.SimExport.Csv.TradesCsv), "Write"), (typeof(XDrishti.SimExport.Json.SimExportJson), "Write") })
        {
            var method = type.GetMethod(name)!;

            method.ReturnType.ShouldBe(typeof(void), type.Name);
            method.GetParameters()[0].ParameterType.ShouldBe(typeof(System.Buffers.IBufferWriter<byte>), type.Name);
        }
    }

    [Fact]
    public void No_class_has_mutable_static_state()
    {
        AssemblyScan.MutableStatics(SimExport).ShouldBeEmpty("static fields must be const or readonly");
        AssemblyScan.MutableStatics(typeof(ClockSentinel).Assembly).ShouldContain(name => name.EndsWith("ClockSentinel.Counter", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_class_is_sealed_or_static()
    {
        Unsealed(SimExport).ShouldBeEmpty("a class designed for inheritance needs a documented reason");
        Unsealed(typeof(ClockSentinel).Assembly).ShouldContain(typeof(UnsealedSentinel).FullName);
    }

    private static List<string?> Unsealed(Assembly assembly) =>
        [.. assembly.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null && t.IsClass && !t.IsSealed).Select(t => t.FullName)];

    // (c) the surface

    [Fact]
    public void The_public_types_are_in_the_documented_namespaces_and_are_exactly_the_known_ones()
    {
        var exported = SimExport.GetExportedTypes();

        exported.Select(t => t.Namespace ?? string.Empty).Distinct().ShouldBeSubsetOf(DocumentedNamespaces);
        exported.Select(t => t.FullName!["XDrishti.SimExport.".Length..]).Order(StringComparer.Ordinal).ShouldBe(
        [
            "Cards.CandleSvg",
            "Cards.CardChoice",
            "Cards.CardNames",
            "Cards.CardSelection",
            "Cards.Html",
            "Cards.IndexHtml",
            "Cards.RuleView",
            "Cards.SeededRank",
            "Cards.TradeCardHtml",
            "Csv.TradesCsv",
            "ExportSpellings",
            "Json.SimExportJson",
            "Manifest.ExportKey",
            "Manifest.ExportManifest",
            "Manifest.ExportNames",
            "Manifest.ManifestEntry",
            "Manifest.ParsedManifest",
            "Manifest.RunId",
            "Text.RunBanner",
            "Text.RunReadme",
            "Verification.EdgeCategories",
            "Verification.PackFiles",
            "Verification.PackIndexHtml",
            "Verification.PackPlan",
            "Verification.PackPlanner",
            "Verification.PackRow",
            "Verification.PackRun",
            "Verification.SampledTrade",
            "Verification.SpotChecks",
            "Verification.SpotRow",
            "Verification.VerificationSampler",
        ]);
        typeof(ClockSentinel).Assembly.GetExportedTypes().Select(t => t.Namespace ?? string.Empty).Distinct().Except(DocumentedNamespaces).ShouldNotBeEmpty("the rule can fail");
    }

    [Fact]
    public void The_json_context_and_its_converters_are_internal()
    {
        SimExport.GetType("XDrishti.SimExport.Json.SimExportJsonContext")!.IsPublic.ShouldBeFalse();
        SimExport.GetTypes().Where(t => t.Namespace == "XDrishti.SimExport.Json" && t.Name.EndsWith("Converter", StringComparison.Ordinal)).ShouldAllBe(t => !t.IsPublic);
    }

    [Fact]
    public void The_domain_types_of_the_labels_and_the_tick_check_are_in_the_domain_and_not_in_the_library()
    {
        SimExport.GetTypes().Select(t => t.Name).Intersect(["RunLabels", "RunLabelSet", "TickUnitCheck", "TickProbe"]).ShouldBeEmpty();
        typeof(XDrishti.Domain.Research.RunLabels).Assembly.GetName().Name.ShouldBe("XDrishti.Domain");
    }
}

/// <summary>Reads a new GUID, the console, the zone of the machine and builds a copy of a whole output, on purpose, to prove the rules of the export library see such a use (it is never used by anything else).</summary>
internal static class ExportSentinel
{
    public static string Make() => Guid.NewGuid().ToString() + TimeZoneInfo.Local.Id + new System.Diagnostics.Process().Id;

    public static void Say() => Console.WriteLine("x");

    public static byte[] Copy()
    {
        using var stream = new MemoryStream();
        stream.WriteByte(1);
        return stream.ToArray();
    }
}
