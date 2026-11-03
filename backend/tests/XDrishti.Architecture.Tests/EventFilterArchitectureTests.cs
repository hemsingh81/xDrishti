using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-06 (ADR 0182): who may read the results calendar and the filings types for the event-risk filter and the loss tag, pinned as exact sets so that a new reader is a deliberate edit. The execution service and the role <c>xd_exec</c> reference and are granted nothing
/// on <c>filings</c>; the execution checklist stays <c>unavailable</c>; the filter calls no execution port and no order type; the loss-tag types are read by no feature, learning model or decision code; the new table is written by one file.
/// </summary>
public sealed partial class EventFilterArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Simulation = Assembly.Load(new AssemblyName("XDrishti.Simulation"));

    private static readonly string[] FilingsNamespaces =
    [
        "XDrishti.Domain.Filings", "XDrishti.Application.Abstractions.Filings", "XDrishti.Application.Filings", "XDrishti.Infrastructure.Filings", "XDrishti.Infrastructure.FilingArchive",
    ];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    [Fact]
    public void The_exact_set_of_application_types_that_name_a_filings_type_is_the_run_the_plan_the_switch_and_the_loss_tag()
    {
        var found = EventFilterUsers.UsersIn(Application, FilingsNamespaces, "XDrishti.Domain.Filings", "XDrishti.Application.Abstractions.Filings", "XDrishti.Application.Filings", "XDrishti.Infrastructure");

        found.Where(n => n is not ("DependencyInjection" or "ApiJsonContext")).ShouldBe([.. EventFilterUsers.ApplicationTypes.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_exact_set_of_domain_and_simulation_types_that_name_a_filings_type_is_the_replay_input_the_loss_tagger_and_the_simulator()
    {
        EventFilterUsers.UsersIn(Domain, FilingsNamespaces, "XDrishti.Domain.Filings").ShouldBe([.. EventFilterUsers.DomainTypes.Order(StringComparer.Ordinal)]);
        EventFilterUsers.UsersIn(Simulation, FilingsNamespaces).ShouldBe([.. EventFilterUsers.SimulationTypes.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_execution_service_references_nothing_of_filings_and_its_sources_and_migrations_grant_it_nothing()
    {
        var execution = Assembly.Load(new AssemblyName("XDrishti.Execution"));
        Types.InAssembly(execution).ShouldNot().HaveDependencyOnAny([.. FilingsNamespaces, "XDrishti.Application.Selection", "XDrishti.Domain.Selection"]).GetResult().IsSuccessful.ShouldBeTrue("XDrishti.Execution");
        foreach (var file in Directory.EnumerateFiles(Src("XDrishti.Execution"), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("filings.", Case.Insensitive, Path.GetFileName(file));
            text.ShouldNotContain("Domain.Filings", Case.Sensitive, Path.GetFileName(file));
        }

        var migration = File.ReadAllText(Src("XDrishti.Infrastructure", "Persistence", "Migrations", "20261102080553_AddEventFilterDecisions.cs"));
        migration.ShouldNotContain("xd_exec");
        migration.ShouldNotContain("xd_mcp");
        migration.ShouldNotContain("GrantSchemaUsageToAppRole");
        migration.ShouldContain("GrantTableToAppRole(Schemas.Filings, \"event_filter_decisions\")");
        migration.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Filings, \"event_filter_decisions\")");
        migration.ShouldContain("SET search_path = pg_catalog, pg_temp");
        migration.ShouldNotContain("SECURITY DEFINER");
    }

    [Fact]
    public void The_execution_checklist_has_no_way_to_receive_the_days_to_results_from_a_calendar()
    {
        // ExecChecklist.Results stays `unavailable` (P15-06 decision 5): its fact is set by nothing in the shipped source but the checklist's own tests.
        foreach (var file in Directory.EnumerateFiles(Src("XDrishti.Execution"), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("IResultsCalendar", Case.Sensitive, Path.GetFileName(file));
            text.ShouldNotContain("ResultsDays", Case.Sensitive, Path.GetFileName(file));
        }

        foreach (var file in Directory.EnumerateFiles(Src("XDrishti.Application"), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("ExecLimits", StringComparison.Ordinal) || text.Contains("ExecChecklist", StringComparison.Ordinal))
            {
                text.ShouldNotContain("IResultsCalendar", Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_filter_the_switch_and_the_snapshot_reach_no_execution_broker_order_or_network_type()
    {
        string[] banned = ["System.Net.Http", "System.Net.Sockets", "XDrishti.Execution", "XDrishti.Infrastructure.Brokers", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Abstractions.LiveTrading"];
        var names = "^(" + string.Join('|', [.. EventFilterUsers.ApplicationTypes.Where(n => n is not ("RunSimulationHandler" or "RunContext" or "SelectionPlanBuilder")), "EventFilterSettings", "EventFilterSwitch", "EventFilterActRules", "EventFilterVerdictReader", "EventCalendarSnapshots", "EventFilterModels"]) + ")$";

        var result = Types.InAssembly(Application).That().HaveNameMatching(names).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Filings").ShouldNot().HaveDependencyOnAny(banned).GetResult().IsSuccessful.ShouldBeTrue();
        foreach (var file in new[] { Src("XDrishti.Application", "Planning", "PlanEventFilter.cs"), Src("XDrishti.Application", "Selection", "ChangeEventFilterHandler.cs"), Src("XDrishti.Application", "Selection", "ApplyEventFilterAutoHandler.cs"), Src("XDrishti.Application", "Filings", "EventCalendarSnapshots.cs") })
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "IBrokerOrders", "IOrderGateway", "IExecution", "PlaceOrder", "HttpClient", "ExecutionCommand" })
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_loss_tag_types_are_read_by_no_feature_model_or_decision_code()
    {
        // ADR 0071, extended to the news_results tag and its event instants (P15-06): a tag describes the past from data known after the trade and must never become a decision-time feature.
        string[] tagTypes = ["XDrishti.Domain.Learning.LossTagKind", "XDrishti.Domain.Learning.LossTagger", "XDrishti.Domain.Learning.LossTag", "XDrishti.Domain.Learning.LossTagReport", "XDrishti.Domain.Filings.EventInstant", "XDrishti.Domain.Filings.ResultsEvents"];
        string[] banned = ["XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Indicators", "XDrishti.Learning.MlNet", "XDrishti.Execution"];

        foreach (var name in banned)
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(tagTypes).GetResult().IsSuccessful.ShouldBeTrue(name);
        }

        string[] decisionAreas =
        [
            "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Portfolio", "XDrishti.Application.Research", "XDrishti.Application.Lab", "XDrishti.Application.Paper", "XDrishti.Application.Live",
        ];
        foreach (var area in decisionAreas)
        {
            var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(tagTypes).GetResult();
            result.IsSuccessful.ShouldBeTrue(area + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        foreach (var domainArea in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation" })
        {
            var result = Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(domainArea).ShouldNot().HaveDependencyOnAny(tagTypes).GetResult();
            result.IsSuccessful.ShouldBeTrue(domainArea + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        // The training set, the feature catalogue and the extractor of the learning code never read a tag.
        foreach (var file in Directory.EnumerateFiles(Src("XDrishti.Domain", "Learning"), "*.cs").Where(f => Path.GetFileName(f).StartsWith("TradeFeature", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("TrainingData", StringComparison.Ordinal)))
        {
            File.ReadAllText(file).ShouldNotContain("LossTag", Case.Sensitive, Path.GetFileName(file));
        }
    }

    [Fact]
    public void The_switch_history_is_written_by_one_file_with_one_insert_and_never_updated_or_deleted()
    {
        foreach (var file in Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("event_filter_decisions", StringComparison.Ordinal))
            {
                continue;
            }

            Path.GetFileName(file).ShouldBeOneOf("EventFilterDecisions.cs", "IEventFilterDecisions.cs", "IEventFilterHistory.cs", "IEventFilterSwitch.cs", "DependencyInjection.cs");
            if (Path.GetFileName(file) == "DependencyInjection.cs")
            {
                continue;   // the registration line only names the table in a comment
            }

            EventFilterDestructive().IsMatch(text).ShouldBeFalse(Path.GetFileName(file));
            if (Path.GetFileName(file) is not ("EventFilterDecisions.cs" or "IEventFilterDecisions.cs" or "IEventFilterHistory.cs"))
            {
                text.ShouldNotContain("INSERT", Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_snapshot_read_is_one_call_before_the_walk_and_the_simulator_does_no_io_and_reads_no_clock()
    {
        foreach (var name in new[] { "Simulator.cs", "SimulatorComparison.cs" })
        {
            var text = File.ReadAllText(Src("XDrishti.Simulation", "Running", name));
            foreach (var word in new[] { "File.", "Directory.", "HttpClient", "Npgsql", "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "await " })
            {
                text.ShouldNotContain(word, Case.Sensitive, name);
            }
        }

        File.ReadAllText(Src("XDrishti.Application", "Research", "RunSimulationHandler.cs")).Split("eventSnapshots.ReadAsync").Length.ShouldBe(2, "exactly one snapshot read per run");
    }

    [Fact]
    public void The_only_verdict_implementation_is_the_reader_of_the_stored_protocol_f_verdicts_which_answers_unavailable_without_one()
    {
        var port = typeof(XDrishti.Application.Abstractions.Selection.IEventFilterVerdict);
        var implementations = Application.GetTypes().Concat(Assembly.Load(new AssemblyName("XDrishti.Infrastructure")).GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(t)).Select(t => t.Name).ToList();

        implementations.ShouldBe(["EventFilterVerdictReader"], "P15-10: the reader of stored protocol-F verdicts is the only implementation; no verdict is unavailable (null), never KEEP");
    }

    [GeneratedRegex(@"\b(UPDATE\s+\w|DELETE\s+FROM|TRUNCATE\s+\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventFilterDestructive();
}
