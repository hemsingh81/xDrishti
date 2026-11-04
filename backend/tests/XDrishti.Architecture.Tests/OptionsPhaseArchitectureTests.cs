using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionPhaseCheck;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.OptionPhaseCheck;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-17 criterion 4 (ADR 0202): the P14 phase check only reads. The handler holds read ports and nothing else, no interface it takes has a write member, the extra reader has exactly the pinned reads and issues bounded SELECTs on the pinned tables with no price, IV, Greek, bar,
/// quote or return column, and the handler, the verb and the pure rule name no write store, setting, switch, evaluation or order. Nothing in a decision area names the check.
/// </summary>
public sealed class OptionsPhaseArchitectureTests
{
    private static readonly Assembly Application = typeof(OptionsPhaseCheckQuery).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly Type[] AllowedParameters =
    [
        typeof(IExtensionEvaluationReader), typeof(IOptionsPhaseReader), typeof(IOptionReportReader), typeof(XDrishti.Application.Abstractions.Messaging.IQueryHandler<,>), typeof(IBarQueries), typeof(ISimulationEngineVersion),
        typeof(ICostSheetHashes), typeof(ICashRunHeaders), typeof(ISpreadModelIds), typeof(IRemarkSettings), typeof(IOptionsPhaseCheckSettings), typeof(TimeProvider), typeof(Microsoft.Extensions.Logging.ILogger<>),
    ];

    private static readonly string[] WriteVerbs = ["Append", "Insert", "Save", "Write", "Store", "Set", "Apply", "Delete", "Update", "Remove", "Record", "Publish", "Place", "Submit", "Enqueue", "Start", "Commit", "Move", "Merge", "Upsert"];

    private static readonly string[] BannedColumns =
    [
        "entry_premium_px", "exit_premium_px", "strike_px", "r_equiv", "cash_r", "gross_inr", "net_inr", "units", "z_entry", "z_exit", "qty_y", "qty_x", "close_px", "open_px", "high_px", "low_px", "bid", "ask", "iv", "delta", "gamma", "vega", "theta",
    ];

    private static Type Handler() => Application.GetType("XDrishti.Application.OptionPhaseCheck.GetOptionsPhaseCheckHandler", throwOnError: true)!;

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static IEnumerable<Type> HandlerPorts() => Handler().GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

    [Fact]
    public void The_handler_takes_only_read_ports()
    {
        HandlerPorts().ShouldAllBe(t => AllowedParameters.Contains(t.IsGenericType ? t.GetGenericTypeDefinition() : t));
        HandlerPorts().ShouldNotContain(typeof(IExtensionEvaluationStore));
        HandlerPorts().ShouldNotContain(typeof(ICostSheetStore));
        HandlerPorts().ShouldNotContain(typeof(IRunStore));
    }

    [Fact]
    public void Every_interface_the_handler_takes_has_no_write_member()
    {
        foreach (var type in HandlerPorts().Where(t => t.IsInterface && t.Namespace?.StartsWith("XDrishti", StringComparison.Ordinal) == true))
        {
            foreach (var method in type.GetInterfaces().Prepend(type).SelectMany(i => i.GetMethods()))
            {
                WriteVerbs.ShouldNotContain(v => method.Name.StartsWith(v, StringComparison.Ordinal), $"{type.Name}.{method.Name}");
            }
        }
    }

    [Fact]
    public void The_extra_ports_have_exactly_the_pinned_reads()
    {
        typeof(IOptionsPhaseReader).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["FetchEndedAtAsync", "IsBackfillOnAsync", "IsChainRecorderOnAsync", "LatestFetchEndedAtAsync", "NonCashCellRowsAsync", "RunInputsAsync"]);
        typeof(IOptionsPhaseReader).GetProperties().ShouldBeEmpty();
        typeof(ICostSheetHashes).GetMethods().Select(m => m.Name).ShouldBe(["RatesHashAsync"]);
        typeof(ICashRunHeaders).GetMethods().Select(m => m.Name).ShouldBe(["FindAsync"]);
        typeof(ISpreadModelIds).GetMethods().Select(m => m.Name).ShouldBe(["LatestAsync"]);
        typeof(IOptionsPhaseCheckSettings).GetProperties().Select(p => p.Name).ShouldBe(["StaleAfterDays"]);
        typeof(IOptionsPhaseReader).GetMethods().Where(m => m.GetParameters().Any(p => p.ParameterType.IsGenericType)).ShouldAllBe(m => m.GetParameters().Any(p => new[] { "runIds", "runKeys", "ids", "underlyingIds" }.Contains(p.Name)));
    }

    [Fact]
    public void The_reader_issues_bounded_selects_on_the_pinned_tables_and_selects_no_price_iv_greek_bar_quote_or_return_column()
    {
        var text = Source("XDrishti.Infrastructure", "OptionPhaseCheck", "OptionsPhaseReader.cs");
        var queries = text.Split("CreateCommand(").Skip(1).Select(c => c[..Math.Min(c.Length, c.IndexOf("command.", StringComparison.Ordinal) is var i and > 0 ? i : c.Length)]).ToList();

        foreach (var banned in new[] { "INSERT", "UPDATE ", "DELETE", "TRUNCATE", "DROP ", "ALTER ", "COPY ", "CREATE ", "ExecuteNonQuery", "BeginBinaryImport", "execution.", "FOR UPDATE" })
        {
            text.ShouldNotContain(banned, Case.Sensitive);
        }

        queries.Count.ShouldBe(6);
        // Each query is bounded by an id list, an underlying list or a limit, or reads the newest row of a switch.
        queries.ShouldAllBe(q => q.Contains("= ANY(@ids)", StringComparison.Ordinal) || q.Contains("LIMIT @limit", StringComparison.Ordinal) || q.Contains("ORDER BY id DESC LIMIT 1", StringComparison.Ordinal) || q.Contains("SELECT max(ended_at) FROM options.fetch_log WHERE outcome IN", StringComparison.Ordinal));
        foreach (var column in BannedColumns)
        {
            Regex.IsMatch(string.Join(' ', queries), @"\b" + column + @"\b", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBeFalse(column);
        }

        Regex.Matches(text, @"(?:FROM|JOIN)\s+(\w+\.\w+)", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(["options.backfill_switch", "options.chain_recorder_switch", "options.fetch_log", "paper.entries", "research.expression_runs", "trading.plan_items"]);
    }

    [Fact]
    public void The_read_adapters_call_exactly_one_member_of_the_store_they_wrap()
    {
        var cash = Source("XDrishti.Infrastructure", "OptionPhaseCheck", "CashRunHeaders.cs");
        var models = Source("XDrishti.Infrastructure", "OptionPhaseCheck", "SpreadModelIds.cs");
        var sheets = Source("XDrishti.Infrastructure", "OptionPhaseCheck", "CostSheetHashes.cs");

        Regex.Matches(cash, @"runs\.(\w+)\(", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ShouldBe(["FindAsync"]);
        Regex.Matches(models, @"models\.(\w+)\(", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ShouldBe(["LatestAsync"]);
        Regex.Matches(sheets, @"sheets\.(\w+)\(", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ShouldBe(["FindAsync"]);
        typeof(ICashRunHeaders).GetMethods().Select(m => m.Name).ShouldBe(["FindAsync"]);
        typeof(ISpreadModelIds).GetMethods().Select(m => m.Name).ShouldBe(["LatestAsync"]);
    }

    [Fact]
    public void The_handler_verb_adapters_and_pure_rule_name_no_write_store_setting_switch_evaluation_or_order()
    {
        var files = new[]
        {
            Source("XDrishti.Application", "OptionPhaseCheck", "GetOptionsPhaseCheckHandler.cs"),
            Source("XDrishti.Api", "Cli", "OptionsPhaseCheckCommand.cs"),
            Source("XDrishti.Api", "Cli", "OptionsPhaseCheckOptions.cs"),
            Source("XDrishti.Infrastructure", "OptionPhaseCheck", "CostSheetHashes.cs"),
            Source("XDrishti.Domain", "Learning", "Extensions", "OptionsPhaseCheck.cs"),
            Source("XDrishti.Domain", "Learning", "Extensions", "OptionsPhaseReport.cs"),
        };

        foreach (var text in files)
        {
            foreach (var name in new[] { "IExtensionEvaluationStore", "ICommandHandler", "AppendAsync", "AppendTrialAsync", "SaveAsync", "StoreAsync", "ITestLedger", "IOptionChainSwitchStore", "IOptionBackfillSwitchStore", "IOptionChainSupervisor", "IOptionBackfillSupervisor", "EvaluationWriter", "IBrokerOrders", "IOrderGateway", "PlaceOrder", "HttpClient", "ILlm" })
            {
                text.ShouldNotContain(name);
            }
        }
    }

    [Fact]
    public void The_rule_is_pure_it_does_no_io_reads_no_clock_and_names_no_application_infrastructure_or_decision_code()
    {
        string[] banned = ["System.IO", "System.Net", "System.Data", "System.TimeProvider", "System.Random", "System.Environment", "Microsoft", "Npgsql", "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Paper", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Options"];

        var result = Types.InAssembly(Domain).That().HaveNameStartingWith("OptionsPhase").Or().HaveName("OptionsRecordFact").Or().HaveName("OptionsFamilyFacts").Or().HaveName("OptionsFamilyLine").ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Domain.GetTypes().Count(t => t.Name.StartsWith("OptionsPhase", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void The_verb_uses_the_query_handler_alone_and_the_handler_is_registered_explicitly()
    {
        Source("XDrishti.Api", "Cli", "OptionsPhaseCheckCommand.cs").ShouldContain("IQueryHandler<OptionsPhaseCheckQuery, Result<OptionsPhaseCheckView>>");
        Source("XDrishti.Application", "DependencyInjection.cs").ShouldContain("IQueryHandler<OptionsPhaseCheckQuery, Result<OptionsPhaseCheckView>>, GetOptionsPhaseCheckHandler>");
        Source("XDrishti.Api", "Cli", "XdCommand.cs").ShouldContain("args[0] == \"options\" && args[1] == \"phase-check\"");
    }

    [Fact]
    public void No_decision_live_execution_or_planning_code_names_the_phase_check()
    {
        foreach (var area in new[] { "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Live", "XDrishti.Application.Paper", "XDrishti.Application.Learning", "XDrishti.Application.LearningPhase" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny("XDrishti.Application.OptionPhaseCheck", "XDrishti.Application.Abstractions.OptionPhaseCheck").GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var name in new[] { "XDrishti.Execution", "XDrishti.Simulation", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Strategies", "XDrishti.Indicators", "XDrishti.Learning.MlNet" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny("XDrishti.Application.OptionPhaseCheck", "XDrishti.Application.Abstractions.OptionPhaseCheck").GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void The_p15_phase_check_does_not_name_the_p14_check()
    {
        Types.InAssembly(Domain).That().HaveName("EventsPhaseCheck").ShouldNot().HaveDependencyOnAny("XDrishti.Domain.Learning.Extensions.OptionsPhaseCheck").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.LearningPhase").ShouldNot().HaveDependencyOnAny("XDrishti.Application.OptionPhaseCheck").GetResult().IsSuccessful.ShouldBeTrue();
    }
}
