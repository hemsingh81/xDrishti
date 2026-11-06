using System.Reflection;
using Microsoft.Extensions.Logging;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-08 (ADR 0211, part A; ADR 0212, part B): the shadow record is forward-only and insert-only (criterion 3), P10's counting types never name it (6), and nothing that chooses what to approve can read shadow outcomes (7). The grader replays only through the one
/// replayer of grading and paper capture (5). The shadow code moves no ticket, builds no simulation request and logs counts only. The no-order tripwire (<see cref="NoOrderPlacementArchitectureTests"/>) covers the new types as well.
/// </summary>
public sealed class ShadowArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] ShadowNamespaces = ["XDrishti.Domain.Shadow", "XDrishti.Application.Shadow", "XDrishti.Application.Abstractions.Shadow"];

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    [Fact]
    public void The_paper_counting_types_of_P10_name_no_shadow_type()
    {
        // criterion 6: the counted set, the phase check, the band and the review live in Domain.Paper and Application.Paper; the owner-approved, armed, primary, measured count cannot see a shadow row
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Paper").GetTypes().ShouldNotBeEmpty();
        var domain = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Paper").Should().NotHaveDependencyOnAny(ShadowNamespaces).GetResult();
        var application = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Paper").Should().NotHaveDependencyOnAny(ShadowNamespaces).GetResult();

        domain.IsSuccessful.ShouldBeTrue(string.Join(", ", domain.FailingTypeNames ?? []));
        application.IsSuccessful.ShouldBeTrue(string.Join(", ", application.FailingTypeNames ?? []));
    }

    [Fact]
    public void Planning_selection_and_autonomy_name_no_shadow_type_and_cannot_read_shadow_outcomes()
    {
        // criterion 7: the rule that chooses what to approve (owner, automatic rule, reserve promotion) must not read shadow results; the writer's own read of the plan goes the other way (Shadow names Planning, never the reverse)
        Types.InAssembly(Application).That().HaveName("WriteShadowHandler").Should().HaveDependencyOn("XDrishti.Application.Planning").GetResult().IsSuccessful.ShouldBeTrue("control: the rule detects a dependency, and the writer reads the plan");
        string[] chooser = ["XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Autonomy", "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Autonomy"];
        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Planning").GetTypes().ShouldNotBeEmpty();
        foreach (var ns in chooser)
        {
            var assembly = ns.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Application;
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny([.. ShadowNamespaces, typeof(IShadowResultReader).FullName!]).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Only_the_writer_and_the_chain_check_name_the_shadow_store_and_only_the_summary_names_the_result_reader()
    {
        var storeUsers = Types.InAssembly(Application).That().HaveDependencyOn(typeof(IShadowStore).FullName!).GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();
        var readerUsers = Types.InAssembly(Application).That().HaveDependencyOn(typeof(IShadowResultReader).FullName!).GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().ToList();
        var gradeStoreUsers = Types.InAssembly(Application).That().HaveDependencyOn(typeof(IShadowGradeStore).FullName!).GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().ToList();

        storeUsers.ShouldBe(["VerifyShadowChainHandler", "WriteShadowHandler"]); // the chain check only reads
        readerUsers.Order(StringComparer.Ordinal).ToList().ShouldBe(["EvaluateDecayHandler", "GetDecayReportHandler", "GetSequentialStatusHandler", "GetShadowSummaryHandler"], "the display use cases of the Api (the summary, CF-08; the sequential failure test, CF-09; the decay report, CF-12) and the daily decay run, which lists the strategies that have shadow candidates (CF-12 part 12b); no Planning, Selection or Autonomy type names the port (above)");
        gradeStoreUsers.ShouldBe(["GradeShadowHandler"]);
    }

    [Fact]
    public void The_chain_check_only_reads_the_store()
    {
        Source("XDrishti.Application", "Shadow", "VerifyShadowChainHandler.cs").ShouldNotContain("WriteSessionAsync");
    }

    [Fact]
    public void The_grader_replays_only_through_the_one_replayer_of_grading_and_paper_capture()
    {
        // criterion 5: GradeShadowHandler names the TicketReplayer and neither the simulation use case nor the strategy and basket ports the replayer hides
        const string Replayer = "XDrishti.Application.Planning.TicketReplayer";
        Types.InAssembly(Application).That().HaveName("GradeShadowHandler").Should().HaveDependencyOn(Replayer).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("GradeShadowHandler").Should().NotHaveDependencyOnAny("XDrishti.Application.Research.RunSimulationQuery", "XDrishti.Application.Abstractions.Research.IStrategyStore", "XDrishti.Application.Abstractions.Market.IBasketQueries").GetResult().IsSuccessful.ShouldBeTrue();
        Source("XDrishti.Application", "Shadow", "GradeShadowHandler.cs").ShouldNotContain("RunSimulationQuery(");
    }

    [Fact]
    public void The_shadow_ports_only_read_and_insert_so_no_code_path_adds_a_row_to_a_session_that_has_opened_or_rewrites_a_grade()
    {
        typeof(IShadowStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order().ShouldBe(["HeadAsync", "ReadChainAsync", "WriteSessionAsync"]);
        typeof(IShadowSettings).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["IsEnabledAsync"]);
        typeof(IShadowResultReader).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order().ShouldBe(["ChainAsync", "ResultsAsync"]);
        typeof(IShadowGradeStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order().ShouldBe(["InsertAsync", "UngradedAsync"]);
        typeof(IShadowSwitchStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["AppendAsync"]);
    }

    [Fact]
    public void The_shadow_adapters_use_only_select_and_insert()
    {
        foreach (var file in new[] { "ShadowStore.cs", "ShadowSettings.cs", "ShadowGradeStore.cs", "ShadowResultReader.cs" })
        {
            var text = Source("XDrishti.Infrastructure", "Paper", file);
            text.ShouldNotContain("UPDATE ", customMessage: file);
            text.ShouldNotContain("DELETE ", customMessage: file);
            text.ShouldNotContain("TRUNCATE", customMessage: file);
            text.ShouldNotContain("ON CONFLICT", customMessage: file); // a stored session or grade is refused, never merged or overwritten
        }

        Types.InAssembly(Infrastructure).That().HaveName("ShadowStore").Or().HaveName("ShadowSettings").Or().HaveName("ShadowGradeStore").Or().HaveName("ShadowResultReader").Should().NotHaveDependencyOnAny("XDrishti.Application.Planning", "XDrishti.Application.Paper").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_shadow_code_moves_no_ticket_builds_no_simulation_request_and_calls_no_broker()
    {
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Shadow", "WriteShadowHandler.cs"))!, "*.cs"))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("MoveItemAsync", customMessage: file);
            text.ShouldNotContain("MovePlanAsync", customMessage: file);
            text.ShouldNotContain("PublishAsync", customMessage: file);
            text.ShouldNotContain("RunSimulationQuery(", customMessage: file);
            text.ShouldNotContain("EnsureFirstEpochAsync", customMessage: file); // it never creates the paper epoch
        }
    }

    [Fact]
    public void The_shadow_domain_types_are_pure_and_the_log_events_carry_counts_only()
    {
        string[] banned = ["System.IO", "System.Net", "Npgsql", "Microsoft.EntityFrameworkCore", "System.TimeProvider", "System.Random"];
        var domain = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Shadow").Should().NotHaveDependencyOnAny(banned).GetResult();
        domain.IsSuccessful.ShouldBeTrue(string.Join(", ", domain.FailingTypeNames ?? []));

        var log = Application.GetType("XDrishti.Application.Shadow.ShadowLog")!;
        var events = log.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null).ToList();
        events.ShouldNotBeEmpty();
        foreach (var method in events)
        {
            method.GetParameters().ShouldAllBe(p => p.ParameterType == typeof(ILogger) || p.ParameterType == typeof(int), method.Name + " logs counts only: no level, feature, symbol or strategy text");
        }
    }
}
