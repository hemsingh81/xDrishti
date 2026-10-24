using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The report engine (P8-05, ADR 0099) is a pure core in <c>XDrishti.Domain.Reporting</c>: no I/O, no clock, no random, no framework; every type is a sealed value, a static function set or an enum; the engine invents no composite score (P8-00 D10): no type or member is
/// named for a health, quality, score or discipline figure; and a report becomes net only through the owner's stored confirmation, never through a flag a request can carry (P8-00 Q19). Each scan has a sentinel, a case the same code must catch.
/// </summary>
public sealed class ReportingArchitectureTests
{
    private const string Namespace = "XDrishti.Domain.Reporting";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] Ambient = ["System.IO", "System.Net", "System.Data", "System.Threading", "System.Diagnostics", "System.TimeProvider", "System.Environment", "System.Random", "System.Security.Cryptography", "System.Text.Json", "Microsoft"];

    private static IEnumerable<Type> ReportingTypes() => Domain.GetTypes().Where(t => t.Namespace == Namespace && !t.Name.StartsWith('<'));

    private static IEnumerable<Type> TopLevel() => ReportingTypes().Where(t => !t.IsNested);

    /// <summary>The wall clock, a new Guid, the environment, random numbers, files, the network and tick counters, read from the IL of the type, its closures and its nested types.</summary>
    private static List<string> AmbientUses(Assembly assembly, Type type) =>
        [.. AssemblyScan.MembersUsedBy(assembly, type.FullName!)
            .Where(c => (c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today")
                || (c.Type == "System.Guid" && c.Member == "NewGuid")
                || c.Type is "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.Diagnostics.Stopwatch" or "System.Environment" or "System.Random" or "System.Security.Cryptography.RandomNumberGenerator")
            .Select(c => c.Type + "." + c.Member)
            .Distinct()];

    [Fact]
    public void The_report_engine_does_no_io_reads_no_clock_and_draws_no_random_number()
    {
        ReportingTypes().ShouldNotBeEmpty();

        var result = Types.InAssembly(Domain).That().ResideInNamespace(Namespace).ShouldNot().HaveDependencyOnAny(Ambient).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var type in TopLevel())
        {
            AmbientUses(Domain, type).ShouldBeEmpty(type.FullName);
        }
    }

    [Fact]
    public void The_ambient_scans_do_see_a_use_where_there_is_one()
    {
        var sentinel = typeof(ReportingAmbientSentinel);

        Types.InAssembly(sentinel.Assembly).That().HaveName(nameof(ReportingAmbientSentinel)).ShouldNot().HaveDependencyOnAny(Ambient).GetResult().IsSuccessful.ShouldBeFalse("the type scan sees the stopwatch and the random number");
        AmbientUses(sentinel.Assembly, sentinel).ShouldBe(["System.DateTime.get_UtcNow", "System.Guid.NewGuid", "System.Diagnostics.Stopwatch.GetTimestamp", "System.Random..ctor", "System.Random.Next"], ignoreOrder: true);
    }

    [Fact]
    public void The_report_engine_types_are_sealed_values_static_functions_or_enums_nested_ones_included()
    {
        ReportingTypes().ShouldAllBe(t => t.IsEnum || t.IsSealed);
    }

    [Fact]
    public void The_engine_invents_no_composite_score()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var names = ReportingTypes().SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(All).Select(m => m.Name)));

        names.Where(Scored).ShouldBeEmpty();
        Scored("LearningHealthScore").ShouldBeTrue("the rule does see a score");
        Scored("QualityIndex").ShouldBeTrue();
        ReportingTypes().Count(t => t.IsNested).ShouldBeGreaterThan(0, "the scan reaches nested types");
    }

    private static readonly string[] ScoreWords = ["Score", "Health", "Discipline", "Quality"];

    private static bool Scored(string name) =>
        ScoreWords.Any(w => name.Replace("equality", string.Empty, StringComparison.OrdinalIgnoreCase).Contains(w, StringComparison.OrdinalIgnoreCase));

    private static bool NamedForNetConfirmation(string name) =>
        name.Contains("Confirm", StringComparison.OrdinalIgnoreCase) && (name.Contains("Net", StringComparison.OrdinalIgnoreCase) || name.Contains("Charge", StringComparison.OrdinalIgnoreCase) || name.Contains("Sheet", StringComparison.OrdinalIgnoreCase));

    /// <summary>Types of the shipped assemblies that carry a request, a query or a command and hold a charge sheet confirmation or a flag named for one.</summary>
    private static List<string> ConfirmationCarriers(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal) || t.Name.EndsWith("Command", StringComparison.Ordinal) || t.Name.EndsWith("Body", StringComparison.Ordinal) || t.Name.EndsWith("Dto", StringComparison.Ordinal)
                || t.Name.EndsWith("Input", StringComparison.Ordinal) || t.Name.EndsWith("Filters", StringComparison.Ordinal) || t.Name.EndsWith("Params", StringComparison.Ordinal))
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(p => p.PropertyType.Name == "ChargeSheetConfirmation" || (p.PropertyType == typeof(bool) && NamedForNetConfirmation(p.Name)))
                || t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType.Name == "ChargeSheetConfirmation" || (p.ParameterType == typeof(bool) && NamedForNetConfirmation(p.Name!))))
            .Select(t => t.FullName!)];

    [Fact]
    public void No_request_type_can_carry_the_charge_sheet_confirmation_so_a_caller_cannot_make_a_report_net()
    {
        // P8-06 hook: the confirmation is read from the stored owner confirmation (ISS-25), never from a field of a request
        ConfirmationCarriers(typeof(XDrishti.Application.DependencyInjection).Assembly).ShouldBeEmpty();
        ConfirmationCarriers(typeof(XDrishti.Infrastructure.DependencyInjection).Assembly).ShouldBeEmpty();
        ConfirmationCarriers(typeof(Program).Assembly).ShouldBeEmpty();
        ConfirmationCarriers(typeof(ConfirmationSentinelRequest).Assembly).ShouldContain(typeof(ConfirmationSentinelRequest).FullName);
        ConfirmationCarriers(typeof(FlagSentinelRequest).Assembly).ShouldContain(typeof(FlagSentinelRequest).FullName);
        ConfirmationCarriers(typeof(InputSentinelInput).Assembly).ShouldContain(typeof(InputSentinelInput).FullName);
    }

    [Fact]
    public void The_report_options_hold_the_confirmation_as_a_value_not_a_flag()
    {
        var options = typeof(XDrishti.Domain.Reporting.ReportOptions);

        options.GetProperty(nameof(XDrishti.Domain.Reporting.ReportOptions.NetBasisConfirmation))!.PropertyType.ShouldBe(typeof(XDrishti.Domain.Reporting.ChargeSheetConfirmation));
        options.GetProperties().ShouldNotContain(p => p.PropertyType == typeof(bool) && p.Name.Contains("Confirm", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Reads the clock, makes a Guid, takes a tick count and draws a random number on purpose, to prove the scans of the report engine see such uses (nothing else uses it).</summary>
internal static class ReportingAmbientSentinel
{
    public static long Use() => DateTime.UtcNow.Ticks + Guid.NewGuid().GetHashCode() + System.Diagnostics.Stopwatch.GetTimestamp() + new Random().Next();
}

/// <summary>A request that carries a charge sheet confirmation, to prove the rule sees one (nothing else uses it).</summary>
internal sealed record ConfirmationSentinelRequest(XDrishti.Domain.Reporting.ChargeSheetConfirmation Confirmation);

/// <summary>A request that carries a confirmation flag, to prove the rule sees one (nothing else uses it).</summary>
internal sealed record FlagSentinelRequest(bool NetConfirmed);

/// <summary>A query-string input that carries a confirmation flag, to prove the rule sees the input types of the report endpoints (nothing else uses it).</summary>
internal sealed record InputSentinelInput(bool ChargeSheetConfirmed);
