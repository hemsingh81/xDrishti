using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Domain.Confidence;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-09 (ADR 0213): the sequential failure test is a pure calculator, its result names no pass, nothing that chooses what to approve (Planning, Selection, Autonomy) can read it or its handler, the P10 counting and phase check types never name it
/// (so the phase check output is untouched), and the handler only reads. The CF-08 rules (<see cref="ShadowArchitectureTests"/>) still hold with the second display reader.
/// </summary>
public sealed class SequentialArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    // every sequential type by full name, found by reflection (the dependency rule matches whole type names or namespaces, not name prefixes): each Domain type "Sequential...", each Application.Shadow view "Sequential...", and the query and handler "GetSequentialStatus..."
    private static readonly string[] Sequential =
    [
        .. Domain.GetTypes().Where(t => t.Namespace == "XDrishti.Domain.Confidence" && t.Name.StartsWith("Sequential", StringComparison.Ordinal)).Select(t => t.FullName!),
        .. Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Shadow" && (t.Name.StartsWith("Sequential", StringComparison.Ordinal) || t.Name.StartsWith("GetSequentialStatus", StringComparison.Ordinal))).Select(t => t.FullName!),
    ];

    private static readonly string[] SequentialTypes = [nameof(SequentialFailureTest), nameof(SequentialOptions), nameof(SequentialResult), nameof(SequentialState), nameof(SequentialTrade), nameof(SequentialWording)];

    [Fact]
    public void The_calculator_is_pure_no_clock_no_random_source_no_io_and_names_no_application_type()
    {
        var result = Types.InAssembly(Domain).That().HaveNameStartingWith("Sequential").Should()
            .NotHaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics", "System.TimeProvider", "System.Environment", "System.Random", "System.Text.Json", "System.Security.Cryptography", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure")
            .GetResult();

        Types.InAssembly(Domain).That().HaveNameStartingWith("Sequential").GetTypes().Count().ShouldBeGreaterThanOrEqualTo(SequentialTypes.Length);
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Confidence", "SequentialFailureTest.cs")).ShouldNotContain("DateTime");
    }

    [Fact]
    public void The_result_types_have_no_member_a_later_story_could_read_as_a_pass()
    {
        // CF-09 criterion 6: a flag only; CF-11 and CF-12 read the state, and no member says passed, proven, ready or approved
        var members = SequentialTypes.Select(n => Domain.GetType("XDrishti.Domain.Confidence." + n)!)
            .Concat([Application.GetType("XDrishti.Application.Shadow.SequentialStatusView")!, Application.GetType("XDrishti.Application.Shadow.SequentialSeriesView")!, Application.GetType("XDrishti.Application.Shadow.SequentialBoundariesView")!, Application.GetType("XDrishti.Application.Shadow.SequentialPathView")!])
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => t.Name + "." + m.Name))
            .Concat(Enum.GetNames<SequentialState>())
            .ToList();

        members.ShouldNotBeEmpty();
        members.ShouldAllBe(m => !m.Contains("pass", StringComparison.OrdinalIgnoreCase) && !m.Contains("proven", StringComparison.OrdinalIgnoreCase) && !m.Contains("ready", StringComparison.OrdinalIgnoreCase) && !m.Contains("approv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Planning_selection_and_autonomy_name_no_sequential_type_and_the_p10_paper_types_do_not_either()
    {
        string[] chooser = ["XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Autonomy", "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Autonomy"];
        foreach (var ns in chooser)
        {
            var assembly = ns.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Application;
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny(Sequential).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        // the phase check and the counted set (P10) are unchanged by this slice: they name nothing sequential (criterion 11)
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Paper").Should().NotHaveDependencyOnAny(Sequential).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Paper").Should().NotHaveDependencyOnAny(Sequential).GetResult().IsSuccessful.ShouldBeTrue();
        Sequential.ShouldContain("XDrishti.Application.Shadow.SequentialPathView"); // control: the list holds the views, the calculator and the handler
        Sequential.ShouldContain("XDrishti.Application.Shadow.GetSequentialStatusHandler");
        Sequential.ShouldContain("XDrishti.Domain.Confidence.SequentialFailureTest");
        Types.InAssembly(Application).That().HaveName("GetSequentialStatusHandler").Should().HaveDependencyOnAny(Sequential).GetResult().IsSuccessful.ShouldBeTrue("control: the rule detects a dependency");
    }

    [Fact]
    public void The_handler_only_reads_no_store_no_clock_no_logger_and_no_command()
    {
        var result = Types.InAssembly(Application).That().HaveName("GetSequentialStatusHandler").Should()
            .NotHaveDependencyOnAny("XDrishti.Application.Abstractions.Shadow.IShadowStore", "XDrishti.Application.Abstractions.Shadow.IShadowGradeStore", "XDrishti.Application.Abstractions.Shadow.IShadowSwitchStore", "XDrishti.Application.Abstractions.Shadow.IShadowSettings", "XDrishti.Application.Planning", "XDrishti.Application.Autonomy", "XDrishti.Application.Abstractions.Execution", "System.TimeProvider", "Microsoft.Extensions.Logging")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Shadow", "GetSequentialStatusHandler.cs"));
        source.ShouldNotContain("ICommandHandler");
        source.ShouldNotContain("AppendAsync");
    }
}
