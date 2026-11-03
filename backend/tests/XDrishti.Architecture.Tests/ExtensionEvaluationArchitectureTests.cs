using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-10 criterion 9: the protocols and the stored verdicts are research only. The protocol code is named by the four evaluations and the verb's handler (and the pure domain); the verdicts are read by the verb, the endpoints and the event-filter verdict reader; nothing in selection, planning,
/// the scorer or the execution service reads them; every evaluation appends its trial to the ledger before it calls its protocol; the new code names no order, broker or network type.
/// </summary>
public sealed class ExtensionEvaluationArchitectureTests
{
    private static readonly string[] Protocols =
    [
        "XDrishti.Domain.Learning.Extensions.ProtocolF", "XDrishti.Domain.Learning.Extensions.ProtocolP", "XDrishti.Domain.Learning.Extensions.ProtocolL", "XDrishti.Domain.Learning.Extensions.ProtocolX",
        "XDrishti.Domain.Learning.Extensions.ExtensionGate", "XDrishti.Domain.Learning.Extensions.ProtocolIdentity",
    ];

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    [Fact]
    public void Only_the_four_evaluations_and_the_pairs_evaluation_name_the_protocols()
    {
        var users = Types.InAssembly(Application).That().HaveDependencyOnAny(Protocols).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["ComparisonEvaluation", "EvaluateExpressionHandler", "EvaluatePairsHandler", "FilterEvaluation", "FlagEvaluation", "PeadEvaluation"]); // EvaluatePairsHandler and EvaluateExpressionHandler: protocols S (P14-13) and O (P14-09) store their record as a ProtocolIdentity
    }

    [Theory]
    [InlineData("XDrishti.Execution")]
    [InlineData("XDrishti.Simulation")]
    [InlineData("XDrishti.Strategies")]
    [InlineData("XDrishti.Indicators")]
    [InlineData("XDrishti.Learning.MlNet")]
    [InlineData("XDrishti.Mcp")]
    [InlineData("XDrishti.Worker")]
    [InlineData("XDrishti.Feed")]
    [InlineData("XDrishti.Api")]
    [InlineData("XDrishti.Infrastructure")]
    public void No_other_assembly_names_a_protocol(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Protocols).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void Only_the_verb_names_the_command_the_endpoints_name_the_queries_and_the_selection_area_names_neither()
    {
        var api = Assembly.Load(new AssemblyName("XDrishti.Api"));
        Types.InAssembly(api).That().HaveDependencyOn("XDrishti.Application.Learning.EvaluateExtensionCommand").GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(["LearnExtensionEvalCommand"]);
        Types.InAssembly(api).That().HaveDependencyOn("XDrishti.Application.Learning.ListExtensionEvaluationsQuery").GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(["ExtensionEvaluationEndpoints"]);
        string[] names = ["XDrishti.Application.Learning.EvaluateExtensionCommand", "XDrishti.Application.Learning.ExtensionEvaluationView", "XDrishti.Application.Learning.ListExtensionEvaluationsQuery", "XDrishti.Application.Learning.EventFilterVerdictReader"];
        foreach (var area in new[] { "XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }
    }

    [Fact]
    public void Every_evaluation_appends_its_trials_to_the_ledger_before_it_calls_its_protocol()
    {
        foreach (var (file, append, protocol) in new[]
        {
            ("FilterEvaluation.cs", "AppendTrialAsync", "ProtocolF.Evaluate"),
            ("ComparisonEvaluation.cs", "AppendTrialAsync", "ProtocolX.Compare"),
            ("PeadEvaluation.cs", "AppendTrialAsync", "ProtocolP.Evaluate"),
        })
        {
            var text = File.ReadAllText(Src("XDrishti.Application", "Learning", file));
            text.IndexOf(append, StringComparison.Ordinal).ShouldBeGreaterThan(0, file);
            text.IndexOf(append, StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf(protocol, StringComparison.Ordinal), file);
        }
    }

    [Fact]
    public void The_hold_out_protocols_are_called_only_inside_the_hold_out_branch()
    {
        foreach (var (file, call) in new[] { ("FilterEvaluation.cs", "ProtocolF.HoldOut("), ("PeadEvaluation.cs", "ProtocolP.HoldOut("), ("ComparisonEvaluation.cs", "ExtensionHoldOutCheck.Check(") })
        {
            var text = File.ReadAllText(Src("XDrishti.Application", "Learning", file));
            var branch = text.IndexOf("if (holdOut)", StringComparison.Ordinal);
            branch.ShouldBeGreaterThan(0, file);
            text.IndexOf(call, StringComparison.Ordinal).ShouldBeGreaterThan(branch, file);
            text.IndexOf("HoldOutUsedAsync", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf(call, StringComparison.Ordinal), file);
        }
    }

    [Fact]
    public void The_new_code_names_no_order_broker_or_network_type_and_the_domain_protocols_read_no_clock_or_io()
    {
        foreach (var path in Directory.EnumerateFiles(Src("XDrishti.Application", "Learning"), "*Evaluation*.cs").Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "Learning"), "EventFilterVerdictReader.cs")))
        {
            var text = File.ReadAllText(path);
            foreach (var word in new[] { "IBrokerOrders", "IOrderGateway", "IExecution", "PlaceOrder", "HttpClient", "ExecutionCommand", "DateTime.Now", "DateTime.UtcNow" })
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(path));
            }
        }

        foreach (var file in new[] { "ProtocolF.cs", "ProtocolP.cs", "ProtocolL.cs", "ProtocolX.cs", "ExtensionGate.cs", "ClusterBootstrap.cs", "SeasonFolds.cs", "FilterObservation.cs", "ProtocolIdentity.cs" })
        {
            var text = File.ReadAllText(Src("XDrishti.Domain", "Learning", "Extensions", file));
            foreach (var word in new[] { "DateTime.Now", "UtcNow", "TimeProvider", "Random.Shared", "new Random", "System.IO", "HttpClient", "Npgsql" })
            {
                text.ShouldNotContain(word, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void The_only_migration_of_this_slice_replaces_the_trigger_function_and_creates_no_table()
    {
        Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*Evaluation*.cs").ShouldBeEmpty();
        var text = File.ReadAllText(Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*_RequireConfirmedKeepForAutoOn.cs").Single());
        text.ShouldContain("CREATE OR REPLACE FUNCTION filings.check_event_filter_decision()");
        text.ShouldNotContain("CREATE TABLE");
        text.ShouldNotContain("GRANT");
        text.ShouldNotContain("xd_exec");
    }
}
