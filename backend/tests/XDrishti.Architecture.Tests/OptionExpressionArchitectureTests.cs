using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionExpression;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-08 criterion 10 (ADR 0197): the option re-mark is research only. Its Domain types are pure; who may name them is an exact set (the use case, its ports, the three adapters); no selection, planning, paper, live or execution code, the execution service, the simulator
/// or any other assembly names them; <c>IRollingBarReader</c> has exactly two bounded reads and its adapter selects from one series over one time range; the simulator's engine version stays <c>sim-9</c>; the migration creates exactly the two tables, insert-only for the runtime
/// role and nothing for any other; no order or broker type is named.
/// </summary>
public sealed class OptionExpressionArchitectureTests
{
    private const string Remark = "XDrishti.Domain.Options.Remark";

    private static readonly string[] AppNamespaces = ["XDrishti.Application.OptionExpression", "XDrishti.Application.Abstractions.OptionExpression"];

    private static readonly string[] OrderWords = ["IBrokerOrders", "IOrderGateway", "IExecution", "PlaceOrder", "HttpClient", "ExecutionCommand", "DateTime.Now", "DateTime.UtcNow"];

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    /// <summary>The top-level type names (nested and compiler-generated types counted under their declaring type) that depend on any of the namespaces, leaving out the types that live in the namespaces named by <paramref name="outside"/>.</summary>
    private static List<string> TopLevelUsers(Assembly assembly, string[] namespaces, string[]? outside = null) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOnAny(namespaces).GetTypes()
            .Where(t => outside is null || !outside.Any(o => (t.Namespace ?? string.Empty).StartsWith(o, StringComparison.Ordinal)))
            .Select(t => (t.DeclaringType ?? t).Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_remark_types_are_pure_they_do_no_io_read_no_clock_and_draw_no_random_number_and_name_no_application_or_decision_code()
    {
        string[] banned =
        [
            "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Threading.Tasks", "System.Reflection", "System.Text.Json", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment",
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Domain.Options.Quality", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution",
        ];

        Domain.GetTypes().Count(t => t.Namespace == Remark).ShouldBeGreaterThan(15);
        var result = Types.InAssembly(Domain).That().ResideInNamespace(Remark).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var file in Directory.GetFiles(Src("XDrishti.Domain", "Options", "Remark"), "*.cs"))
        {
            Regex.IsMatch(File.ReadAllText(file), @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|Environment\.|File\.|Stopwatch)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_remark_type_is_public_and_sealed_or_static_or_an_enum_and_declared_in_a_file_of_its_name()
    {
        var dir = Src("XDrishti.Domain", "Options", "Remark");
        foreach (var type in Domain.GetTypes().Where(t => t.Namespace == Remark && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal)))
        {
            type.IsPublic.ShouldBeTrue(type.Name);
            (type.IsSealed || type.IsEnum).ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }
    }

    [Fact]
    public void The_exact_set_of_types_that_name_a_remark_type_is_the_use_case_its_ports_and_the_adapters()
    {
        TopLevelUsers(Application, [Remark]).ShouldBe(["IContractExpiryReader", "IExpressionTradeStore", "IRollingBarReader", "IvFeatureExtractor", "RemarkRunHandler", "RemarkShared", "RemarkStructureHandler", "SimulateCondorsHandler"]); // IvFeatureExtractor: the iv-1 extraction (P14-11) reads the bars through the same port; RemarkStructureHandler, SimulateCondorsHandler and their shared helpers: the structures (P14-10); (EvaluateExpressionHandler reads the ExpiryRules symbol constants, which the compiler inlines)
        TopLevelUsers(Infrastructure, [Remark]).ShouldBe(["ContractExpiryReader", "ExpressionTradeStore", "RollingBarReader"]);
        TopLevelUsers(Assembly.Load(new AssemblyName("XDrishti.Api")), [Remark]).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_wiring_the_verb_and_the_use_case_name_the_application_or_infrastructure_remark_namespaces()
    {
        TopLevelUsers(Application, AppNamespaces, AppNamespaces).ShouldBe(["DependencyInjection", "GetOptionsPhaseCheckHandler", "IvFeatureExtractor"]); // GetOptionsPhaseCheckHandler (P14-17): reads the remark settings' condor sheet id; IvFeatureExtractor: the iv-1 extraction (P14-11) reads the rolling bars and the contract expiries through their ports
        TopLevelUsers(Infrastructure, AppNamespaces).ShouldBe(["ContractExpiryReader", "DependencyInjection", "ExpressionReader", "ExpressionTradeStore", "NoRegimeLabels", "RemarkSettings", "RollingBarReader"]); // NoRegimeLabels: the regime port (P14-10) until P6-02 stores labels
        TopLevelUsers(Assembly.Load(new AssemblyName("XDrishti.Api")), AppNamespaces).ShouldBe(["OptionsCondorsCommand", "OptionsExpressionEvalCommand", "OptionsRemarkCommand"]);
    }

    [Theory]
    [InlineData("XDrishti.Execution")]
    [InlineData("XDrishti.Simulation")]
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
    public void No_other_assembly_names_a_remark_type_or_one_of_its_ports(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny([Remark, .. AppNamespaces]).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void No_decision_area_of_the_domain_or_the_application_names_a_remark_type_or_one_of_its_ports()
    {
        string[] names = [Remark, .. AppNamespaces];
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
    public void The_pricing_core_is_named_only_inside_the_remark_namespace_among_the_new_code()
    {
        Types.InAssembly(Application).ShouldNot().HaveDependencyOn("XDrishti.Domain.Options.Pricing").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("XDrishti.Domain.Options.Pricing").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Domain).That().ResideInNamespace(Remark).Should().HaveDependencyOn("XDrishti.Domain.Options.Pricing").GetResult().IsSuccessful.ShouldBeFalse("only some types name it, so the 'every type' form fails: the rule is not vacuous");
        Types.InAssembly(Domain).That().ResideInNamespace(Remark).And().HaveName("OptionRemark").Should().HaveDependencyOn("XDrishti.Domain.Options.Pricing").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_rolling_bar_reader_has_bounded_reads_selects_one_series_over_one_time_range_and_adds_the_iv_reads_of_p14_11()
    {
        typeof(IRollingBarReader).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["BarsAsync", "BarsAsync", "DailyLastBarsAsync", "HasSeriesAsync", "WatermarkAsync"]); // P14-11: the offset-filtered overload, the grouped daily read and the existence query for the iv-1 extraction
        typeof(IRollingBarReader).GetProperties().ShouldBeEmpty();
        var sql = File.ReadAllText(Src("XDrishti.Infrastructure", "OptionExpression", "RollingBarReader.cs"));

        sql.ShouldContain("underlying_id = @u AND expiry_flag = @flag AND expiry_code = @code AND side = @side AND interval_minutes = @interval AND ts >= @from AND ts < @to");
        sql.ShouldContain("MaxRangeDays");
        sql.ShouldContain("MaxDailyRangeDays");
        sql.ShouldContain("atm_offset = 0 AND ts >= @from AND ts < @to");
        sql.ShouldContain("SELECT EXISTS (SELECT 1 FROM options.rolling_bars WHERE underlying_id = @u)");
        Regex.Matches(sql, @"FROM options\.(\w+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ShouldBe(["fetch_log", "rolling_bars"]);
        foreach (var banned in new[] { "INSERT", "UPDATE ", "DELETE", "TRUNCATE", "DROP " })
        {
            sql.ShouldNotContain(banned, Case.Sensitive);
        }

        File.ReadAllText(Src("XDrishti.Infrastructure", "OptionExpression", "ContractExpiryReader.cs")).ShouldContain("WHERE underlying_id = @u AND expiry >= @from AND expiry <= @to");
    }

    [Fact]
    public void The_simulator_engine_version_stays_sim_9_and_the_simulator_does_not_name_the_remark()
    {
        XDrishti.Simulation.SimulationEngine.Version.ShouldBe("sim-9");
        Directory.EnumerateFiles(Src("XDrishti.Simulation"), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f))
            .Where(f => File.ReadAllText(f).Contains("Options.Remark", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_remark_code_names_no_order_broker_or_network_type_and_no_clock()
    {
        var files = Directory.EnumerateFiles(Src("XDrishti.Domain", "Options", "Remark"), "*.cs").Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "OptionExpression"), "*.cs")).Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "Abstractions", "OptionExpression"), "*.cs"))
            .Concat(Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "OptionExpression"), "*.cs")).Concat([Src("XDrishti.Api", "Cli", "OptionsRemarkCommand.cs"), Src("XDrishti.Api", "Cli", "OptionsCondorsCommand.cs")]).Concat(Directory.EnumerateFiles(Src("XDrishti.Domain", "Options", "Structures"), "*.cs"));
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
    public void Only_the_store_the_migration_and_the_comments_of_the_ports_name_the_two_tables_and_the_migration_is_insert_only_for_the_runtime_role_and_nothing_for_any_other()
    {
        var named = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.EndsWith(".Designer.cs", StringComparison.Ordinal) && Regex.IsMatch(File.ReadAllText(f), @"expression_runs|expression_trades|expression_legs", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)))
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList();
        var migration = File.ReadAllText(Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*_AddExpressionTrades.cs").Single());

        named.ShouldBe(["20261103183027_AddExpressionTrades.cs", "20261103224548_AddExpressionLegs.cs", "DependencyInjection.cs", "ExpressionReader.cs", "ExpressionRunRow.cs", "ExpressionTradeStore.cs", "IExpressionTradeStore.cs", "OptionReportReader.cs", "OptionsPhaseReader.cs", "StructureLegRow.cs"]); // OptionReportReader (P14-16, ADR 0201) reads run counts with select only; StructureLegRow: its comment names the legs table (P14-10); the migration and the store write them; the wiring, the port and the run row name them in comments only; ExpressionReader (P14-09) reads them with select only
        migration.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Research, \"expression_runs\")");
        migration.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Research, \"expression_trades\")");
        migration.ShouldNotContain("xd_exec");
        migration.ShouldNotContain("GrantSchemaUsageToAppRole");
        migration.ShouldContain("DROP TABLE IF EXISTS research.expression_trades");
        migration.ShouldContain("DROP TABLE IF EXISTS research.expression_runs");

        // P14-10 (ADR 0200): the legs table is insert-only for the runtime role too, nothing for the execution role, and Down deletes no row
        var legs = File.ReadAllText(Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*_AddExpressionLegs.cs").Single());
        legs.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Research, \"expression_legs\")");
        legs.ShouldNotContain("xd_exec");
        legs.ShouldNotContain("GrantSchemaUsageToAppRole");
        legs.ShouldContain("DROP TABLE IF EXISTS research.expression_legs");
        legs.ShouldNotContain("DELETE FROM");
    }
}
