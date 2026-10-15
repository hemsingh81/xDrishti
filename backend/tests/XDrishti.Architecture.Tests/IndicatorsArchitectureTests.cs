using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The indicator library (P3-01, ADR 0031) is a pure engine module: it references only Domain, knows no database, framework, HTTP, file or clock, keeps no mutable static state, and every public class is sealed.
/// The same indicator code runs in back-tests and live, so none of those can be allowed to creep in.
/// </summary>
public sealed class IndicatorsArchitectureTests
{
    private static readonly Assembly Indicators = typeof(XDrishti.Indicators.IIndicator).Assembly;

    private static readonly string[] ForbiddenNamespaces =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
        "System.Net", "System.IO", "System.Data", "System.Threading.Tasks", "System.Diagnostics",
        "System.TimeProvider", "System.Environment", "System.Random",
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_library_references_only_the_domain_among_the_projects_and_no_framework_package()
    {
        var referenced = Indicators.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referenced.Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).ShouldBe(["XDrishti.Domain"]);
        referenced.ShouldNotContain(n => n.StartsWith("Microsoft.", StringComparison.Ordinal) && n != "Microsoft.CSharp", "no Microsoft.* package (EF Core, ASP.NET Core, Extensions)");
        referenced.ShouldNotContain(n => n.StartsWith("Npgsql", StringComparison.Ordinal) || n.StartsWith("Serilog", StringComparison.Ordinal));
        referenced.ShouldNotContain("System.Net.Http");
        referenced.ShouldNotContain("System.Net.Primitives");
    }

    [Fact]
    public void No_type_depends_on_other_layers_the_database_http_files_or_a_clock()
    {
        ShouldHaveNone(Types.InAssembly(Indicators).ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces).GetResult());
    }

    [Fact]
    public void Nothing_reads_the_clock_or_touches_files_or_the_network()
    {
        var calls = MetadataScan.MemberReferences(Indicators);

        calls.Where(c => c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today").ShouldBeEmpty("DateTime.Now / UtcNow / Today");
        calls.Where(c => c.Type is "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.TimeProvider").ShouldBeEmpty("File, Directory, HttpClient, TimeProvider");
        calls.Where(c => c.Type is "System.Diagnostics.Stopwatch" or "System.Environment").ShouldBeEmpty("tick counters");
    }

    [Fact]
    public void The_scan_does_find_a_clock_read_when_there_is_one()
    {
        // ClockSentinel below reads DateTime.UtcNow and File.Exists on purpose: the scan used above must see both in this assembly.
        var calls = MetadataScan.MemberReferences(typeof(ClockSentinel).Assembly);

        calls.ShouldContain(c => c.Type == "System.DateTime" && c.Member == "get_UtcNow");
        calls.ShouldContain(c => c.Type == "System.IO.File");
        ClockSentinel.Read().ShouldBeGreaterThan(DateTime.MinValue);
        ClockSentinel.Exists().ShouldBeFalse();
    }

    [Fact]
    public void No_class_has_mutable_static_state() => MutableStatics(Indicators).ShouldBeEmpty("static fields must be const or readonly");

    [Fact]
    public void The_static_state_rule_and_the_dependency_rule_do_catch_a_violation()
    {
        MutableStatics(typeof(ClockSentinel).Assembly).ShouldContain(name => name.EndsWith("ClockSentinel.Counter", StringComparison.Ordinal));
        Types.InAssembly(typeof(ClockSentinel).Assembly).That().HaveName(nameof(ClockSentinel)).ShouldNot().HaveDependencyOnAny("System.IO").GetResult().IsSuccessful.ShouldBeFalse();
    }

    private static List<string> MutableStatics(Assembly assembly)
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

    [Fact]
    public void Every_public_class_is_sealed_or_static()
    {
        var offenders = Indicators.GetExportedTypes().Where(t => t.IsClass && !t.IsSealed).Select(t => t.FullName).ToList();

        offenders.ShouldBeEmpty("a public class designed for inheritance needs a documented reason; none exists in this library");
    }

    [Fact]
    public void The_indicator_contract_exposes_only_domain_and_system_types()
    {
        ShouldHaveNone(Types.InAssembly(Indicators).That().ArePublic().ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft", "Npgsql").GetResult());
    }

    /// <summary>Reads the member and type references recorded in an assembly's metadata: every method or property of another type that its code calls.</summary>
    private static class MetadataScan
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
    }
}

/// <summary>Reads the clock and a file on purpose, to prove the scan above sees such calls (it is never used by anything else).</summary>
internal static class ClockSentinel
{
    internal static int Counter;

    public static DateTime Read()
    {
        Counter++;
        return DateTime.UtcNow;
    }

    public static bool Exists() => File.Exists("/definitely/not/here");
}
