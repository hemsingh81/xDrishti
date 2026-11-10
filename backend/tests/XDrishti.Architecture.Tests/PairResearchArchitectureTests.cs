using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-13 criterion 9 (ADR 0196): the pairs study is research only. Its pure types are pure; who may name them is an exact set (the two verbs' handlers and ports, the store and the walker adapter, the host that parses the line, the walker); nothing in selection, planning, paper, live trading or the
/// execution service reads them; the handlers keep the ledger-first order; the migration creates exactly the two tables, insert-only for the runtime role and nothing for any other; no order or broker type is named.
/// </summary>
public sealed class PairResearchArchitectureTests
{
    private const string Study = "XDrishti.Domain.PairResearch";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] OrderWords = ["IBrokerOrders", "IOrderGateway", "IExecution", "PlaceOrder", "HttpClient", "ExecutionCommand", "DateTime.Now", "DateTime.UtcNow"];

    private static readonly string[] RunOrder = ["PairTrial.Variant(", "universe.ListAsync(", "PairUniverse.Candidates(", "PairTrial.Candidate(", "PairFormation.Form(", "store.AppendFormationAsync(", "simulator.Walk(", "store.AppendTradesAsync("];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static string Text(params string[] parts) => File.ReadAllText(Src(parts));

    private static List<string> TopLevelUsers(Assembly assembly) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(Study).GetTypes().Select(t => (t.DeclaringType ?? t).Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_study_types_are_pure_they_do_no_io_read_no_clock_and_draw_no_random_number_and_name_no_application_or_decision_code()
    {
        string[] banned =
        [
            "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Threading.Tasks", "System.Reflection", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment",
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution",
        ];

        Domain.GetTypes().Count(t => t.Namespace == Study).ShouldBeGreaterThan(20);
        var result = Types.InAssembly(Domain).That().ResideInNamespace(Study).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var file in Directory.GetFiles(Src("XDrishti.Domain", "PairResearch"), "*.cs"))
        {
            Regex.IsMatch(File.ReadAllText(file), @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|Environment\.|File\.|Stopwatch)", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_study_type_is_public_and_sealed_or_static_or_an_enum_and_declared_in_a_file_of_its_name()
    {
        var dir = Src("XDrishti.Domain", "PairResearch");
        foreach (var type in Domain.GetTypes().Where(t => t.Namespace == Study && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal)))
        {
            type.IsPublic.ShouldBeTrue(type.Name);
            (type.IsSealed || type.IsEnum).ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }
    }

    [Fact]
    public void The_exact_set_of_types_that_name_a_study_type_is_the_two_handlers_the_ports_the_store_the_adapter_the_verb_and_the_walker()
    {
        TopLevelUsers(Application).ShouldBe(["EvaluatePairsHandler", "IPairSimulator", "PairConfig", "RunPairsCommand", "RunPairsHandler", "StoredFormation"]);
        TopLevelUsers(Infrastructure).ShouldBe(["PairResearchStore", "PairSimulator"]);
        TopLevelUsers(Assembly.Load(new AssemblyName("XDrishti.Api"))).ShouldBe(["PairsCommand"]);
        TopLevelUsers(Assembly.Load(new AssemblyName("XDrishti.Simulation"))).ShouldBe(["PairWalker"]);
    }

    [Theory]
    [InlineData("XDrishti.Execution")]
    [InlineData("XDrishti.Feed")]
    [InlineData("XDrishti.Worker")]
    [InlineData("XDrishti.Mcp")]
    [InlineData("XDrishti.Strategies")]
    [InlineData("XDrishti.Strategies.File")]
    [InlineData("XDrishti.Indicators")]
    [InlineData("XDrishti.Learning.MlNet")]
    [InlineData("XDrishti.SimExport")]
    [InlineData("XDrishti.SimReader")]
    [InlineData("XDrishti.Hosting")]
    public void No_other_assembly_names_a_study_type(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOn(Study).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void No_decision_area_of_the_domain_or_the_application_names_a_study_type_or_one_of_its_ports()
    {
        string[] names = [Study, "XDrishti.Application.Pairs", "XDrishti.Application.Abstractions.Pairs"];
        foreach (var area in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation" })
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in new[] { "XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live", "XDrishti.Application.Learning" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }
    }

    [Fact]
    public void The_run_handler_appends_the_variants_then_every_candidate_to_the_ledger_before_any_statistic_and_stores_the_formation_before_any_walk()
    {
        var text = Text("XDrishti.Application", "Pairs", "RunPairsHandler.cs");

        var order = RunOrder.Select(w => text.IndexOf(w, StringComparison.Ordinal)).ToList();

        order.ShouldAllBe(i => i > 0);
        order.ShouldBe(order.Order());
    }

    [Fact]
    public void The_evaluation_reads_the_ledger_before_any_trade_and_reads_the_hold_out_only_inside_its_branch_after_the_spent_check()
    {
        var text = Text("XDrishti.Application", "Pairs", "EvaluatePairsHandler.cs");

        text.IndexOf("ledger.HasHashAsync(", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        text.IndexOf("ledger.HasHashAsync(", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("store.ReadTradesAsync(", StringComparison.Ordinal));
        text.IndexOf("store.ReadTradesAsync(", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("ProtocolS.Evaluate(", StringComparison.Ordinal));
        text.IndexOf("HoldOutUsedAsync(", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("ledger.HasHashAsync(", StringComparison.Ordinal));
        text.IndexOf("ProtocolS.Cut(", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("ProtocolS.Evaluate(", StringComparison.Ordinal));
        text.IndexOf("if (command.HoldOut)", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("ProtocolS.HoldOut(", StringComparison.Ordinal));
        text.ShouldNotContain("AppendAsync(new LedgerRow", Case.Sensitive);
    }

    [Fact]
    public void The_study_code_names_no_order_broker_or_network_type_and_no_clock()
    {
        var files = Directory.EnumerateFiles(Src("XDrishti.Application", "Pairs"), "*.cs").Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "Abstractions", "Pairs"), "*.cs")).Concat(Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Pairs"), "*.cs"))
            .Concat([Src("XDrishti.Simulation", "Walking", "PairWalker.cs"), Src("XDrishti.Api", "Cli", "PairsCommand.cs")]);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in OrderWords)
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void Only_the_store_the_migration_and_the_comments_of_the_port_name_the_two_tables_and_the_migration_is_insert_only_for_the_runtime_role_and_nothing_for_any_other()
    {
        var named = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && Regex.IsMatch(File.ReadAllText(f), @"pair_formations|pair_trades", RegexOptions.CultureInvariant, ScanRegex.Timeout))
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList();
        var migration = File.ReadAllText(Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*_AddPairResearch.cs").Single());

        named.ShouldBe(["20261103130000_AddPairResearch.cs", "DependencyInjection.cs", "IPairResearchStore.cs", "OptionReportReader.cs", "PairFormationRow.cs", "PairResearchStore.cs", "PairTradeRow.cs"]); // the migration and the store write them; OptionReportReader (P14-16, ADR 0201) reads them with select only; the wiring, the port and its two row types name them in comments only
        migration.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Research, \"pair_formations\")");
        migration.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Research, \"pair_trades\")");
        migration.ShouldNotContain("xd_exec");
        migration.ShouldNotContain("GrantSchemaUsageToAppRole");
        migration.ShouldContain("DROP TABLE IF EXISTS research.pair_trades");
        migration.ShouldContain("DROP TABLE IF EXISTS research.pair_formations");
    }
}
