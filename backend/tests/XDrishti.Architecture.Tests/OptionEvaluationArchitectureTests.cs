using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionExpression;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-09 criterion 9 (ADR 0198): the expression evaluation is research only. Its pure types are pure; who may name them (and the family list) is an exact set (the use case, its port, the reader, the shared route and its error text); nothing in selection, planning, paper, live trading or the execution service
/// reads them; the P15 phase check does not name the family; the use case keeps the ledger-first order and the hold-out guard first; the reader is select only, filters by the windows in the query, and has four reads; no order or broker type is named.
/// </summary>
public sealed class OptionEvaluationArchitectureTests
{
    private const string Evaluation = "XDrishti.Domain.Options.Evaluation";
    private const string Families = "XDrishti.Domain.Learning.Extensions.ExpressionFamilies";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = Assembly.Load(new AssemblyName("XDrishti.Api"));

    private static readonly string[] OrderWords = ["IBrokerOrders", "IOrderGateway", "IExecution", "PlaceOrder", "HttpClient", "ExecutionCommand", "DateTime.Now", "DateTime.UtcNow"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Users(Assembly assembly, string dependency, string? outside = null) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(dependency).GetTypes()
            .Where(t => outside is null || !(t.Namespace ?? string.Empty).StartsWith(outside, StringComparison.Ordinal))
            .Select(t => (t.DeclaringType ?? t).Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_evaluation_types_are_pure_they_do_no_io_read_no_clock_and_draw_no_random_number_and_name_no_application_or_decision_code()
    {
        string[] banned =
        [
            "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Threading.Tasks", "System.Reflection", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment",
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution",
        ];

        Domain.GetTypes().Count(t => t.Namespace == Evaluation).ShouldBeGreaterThan(8);
        var result = Types.InAssembly(Domain).That().ResideInNamespace(Evaluation).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var file in Directory.GetFiles(Src("XDrishti.Domain", "Options", "Evaluation"), "*.cs"))
        {
            Regex.IsMatch(File.ReadAllText(file), @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|Environment\.|File\.|Stopwatch)", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_evaluation_type_is_public_and_sealed_or_static_or_an_enum_and_declared_in_a_file_of_its_name()
    {
        var dir = Src("XDrishti.Domain", "Options", "Evaluation");
        foreach (var type in Domain.GetTypes().Where(t => t.Namespace == Evaluation && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal)))
        {
            type.IsPublic.ShouldBeTrue(type.Name);
            (type.IsSealed || type.IsEnum).ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }
    }

    [Fact]
    public void The_exact_set_of_types_that_name_an_evaluation_type_is_the_use_case_its_port_and_the_reader()
    {
        Users(Application, Evaluation).ShouldBe(["EvaluateExpressionHandler", "ExpressionDates", "GetOptionsPhaseCheckHandler", "IExpressionReader"]); // GetOptionsPhaseCheckHandler (P14-17): the pre-registered primary profile hash of a family
        Users(Infrastructure, Evaluation).ShouldBe(["ExpressionReader"]);
        Users(Api, Evaluation).ShouldBeEmpty();
        Users(Domain, Evaluation, Evaluation).ShouldBeEmpty();
    }

    [Fact]
    public void The_exact_set_of_types_that_name_the_expression_family_list_is_the_use_case_the_shared_route_and_its_error_text()
    {
        Users(Domain, Families).ShouldBeEmpty(); // ProtocolO reads only the protocol letter, a constant the compiler inlines
        Users(Application, Families).ShouldBe(["EvaluateExpressionHandler", "ExtensionEvaluationErrors", "GetP14VerdictListHandler", "ListExtensionEvaluationsHandler"]); // GetP14VerdictListHandler (P14-16): the verdict list
        Users(Infrastructure, Families).ShouldBeEmpty();
        Users(Api, Families).ShouldBeEmpty();
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
    public void No_other_assembly_names_an_evaluation_type_the_family_list_the_use_case_or_its_port(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Evaluation, Families, "XDrishti.Application.OptionExpression.EvaluateExpressionCommand", "XDrishti.Application.Abstractions.OptionExpression.IExpressionReader").GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void No_decision_area_and_not_the_p15_phase_check_names_an_evaluation_type_the_family_list_or_the_use_case()
    {
        string[] names = [Evaluation, Families, "XDrishti.Application.OptionExpression.EvaluateExpressionCommand", "XDrishti.Application.Abstractions.OptionExpression.IExpressionReader", "XDrishti.Domain.Learning.Extensions.ExpressionFamilies"];
        foreach (var area in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation" })
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in new[] { "XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live", "XDrishti.Application.LearningPhase" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        Types.InAssembly(Domain).That().HaveName("EventsPhaseCheck").ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_use_case_refuses_a_spent_hold_out_first_then_appends_the_trial_reads_it_back_and_only_then_reads_a_date_a_count_or_a_figure_and_the_hold_out_rows_come_last()
    {
        var text = File.ReadAllText(Src("XDrishti.Application", "OptionExpression", "EvaluateExpressionHandler.cs"));
        string[] order =
        [
            "evaluations.HoldOutUsedAsync(", "profiles.GetAsync(", "runs.FindAsync(", "reader.FindRunAsync(", "writer.AppendTrialAsync(", "ledger.HasHashAsync(", "reference.EpochsAsync(", "reader.DatesAsync(", "reader.PairedAsync(remarked.RunId, devWindows", "reader.UnpairedAsync(", "ProtocolO.Evaluate(", "ProtocolO.HoldOut(",
        ];
        var last = -1;
        foreach (var call in order)
        {
            var at = text.IndexOf(call, StringComparison.Ordinal);
            at.ShouldBeGreaterThan(last, call);
            last = at;
        }

        text.ShouldContain("reader.PairedAsync(remarked.RunId, ExpressionWindows.From(windows, start)");
        text.IndexOf("ExpressionWindows.From(", StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("ProtocolO.Evaluate(", StringComparison.Ordinal));
        text.ShouldNotContain("DateTime.Now");
    }

    [Fact]
    public void The_reader_has_four_reads_is_select_only_reads_the_two_tables_and_every_figure_read_filters_by_side_and_entry_instant_in_the_query()
    {
        typeof(IExpressionReader).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["DatesAsync", "FindRunAsync", "PairedAsync", "UnpairedAsync"]);
        typeof(IExpressionReader).GetProperties().ShouldBeEmpty();
        var sql = File.ReadAllText(Src("XDrishti.Infrastructure", "OptionExpression", "ExpressionReader.cs"));

        Regex.Matches(sql, @"FROM research\.(\w+)", RegexOptions.CultureInvariant, ScanRegex.Timeout).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ShouldBe(["expression_runs", "expression_trades"]);
        foreach (var banned in new[] { "INSERT", "UPDATE ", "DELETE", "TRUNCATE", "DROP " })
        {
            sql.ShouldNotContain(banned, Case.Sensitive);
        }

        sql.ShouldContain("(side = @s{i} AND entry_ts >= @a{i} AND entry_ts < @b{i})");
        Regex.Count(sql, @"AND \(""? \+ Predicate\(windows\)", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBe(2);
        sql.ShouldContain("ToUniversalTime()");
    }

    [Fact]
    public void The_new_code_names_no_order_broker_or_network_type_and_no_clock_and_this_slice_adds_no_migration()
    {
        var files = Directory.EnumerateFiles(Src("XDrishti.Domain", "Options", "Evaluation"), "*.cs").Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "OptionExpression"), "Evaluate*.cs")).Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "OptionExpression"), "ExpressionEvalLog.cs"))
            .Concat([Src("XDrishti.Application", "Abstractions", "OptionExpression", "IExpressionReader.cs"), Src("XDrishti.Infrastructure", "OptionExpression", "ExpressionReader.cs"), Src("XDrishti.Api", "Cli", "OptionsExpressionEvalCommand.cs"), Src("XDrishti.Domain", "Learning", "Extensions", "ExpressionFamilies.cs")]);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in OrderWords)
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }

        Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*.cs").Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal)).Select(Path.GetFileName)
            .Count(n => n!.Contains("ExpressionEval", StringComparison.Ordinal) || n.Contains("ProtocolO", StringComparison.Ordinal)).ShouldBe(0);
    }
}
