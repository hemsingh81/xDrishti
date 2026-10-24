using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The report engine (P8-05, ADR 0099) is a pure core in <c>XDrishti.Domain.Reporting</c>: no I/O, no clock, no random, no framework; every type is a sealed value, a static function set or an enum; and the engine invents no composite score (P8-00 D10): no
/// type or member is named for a health, quality or discipline score.
/// </summary>
public sealed class ReportingArchitectureTests
{
    private const string Namespace = "XDrishti.Domain.Reporting";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static IEnumerable<Type> ReportingTypes() => Domain.GetTypes().Where(t => t.Namespace == Namespace && !t.IsNested && !t.Name.StartsWith('<'));

    [Fact]
    public void The_report_engine_does_no_io_reads_no_clock_and_draws_no_random_number()
    {
        ReportingTypes().ShouldNotBeEmpty();

        var result = Types.InAssembly(Domain).That().ResideInNamespace(Namespace)
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading", "System.Diagnostics", "System.TimeProvider", "System.Environment", "System.Random", "System.Security.Cryptography", "System.Text.Json", "Microsoft")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_report_engine_types_are_sealed_values_static_functions_or_enums()
    {
        ReportingTypes().ShouldAllBe(t => t.IsEnum || t.IsSealed);
    }

    [Fact]
    public void The_engine_invents_no_composite_score()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var names = ReportingTypes().SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(All).Select(m => m.Name)));

        names.ShouldNotContain(n => n.Contains("Score", StringComparison.OrdinalIgnoreCase) || n.Contains("Health", StringComparison.OrdinalIgnoreCase) || n.Contains("Discipline", StringComparison.OrdinalIgnoreCase));
    }
}
