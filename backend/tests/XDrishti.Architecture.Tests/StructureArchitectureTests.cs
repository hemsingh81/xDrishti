using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-10 criterion 8 (ADR 0200): the defined-risk structures are research only. Their Domain types are pure and sealed, one per file; who may name them is an exact set; no selection, planning, paper, live trading, the execution service, the simulator or any other assembly names them; they read no clock and name no order,
/// broker, margin or network type; every short leg is built beside a long leg (no naked short is constructible by the type that builds legs).
/// </summary>
public sealed class StructureArchitectureTests
{
    private const string Structures = "XDrishti.Domain.Options.Structures";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = Assembly.Load(new AssemblyName("XDrishti.Api"));

    private static readonly string[] OrderWords = ["IBrokerOrders", "IOrderGateway", "IExecution", "PlaceOrder", "HttpClient", "ExecutionCommand", "DateTime.Now", "DateTime.UtcNow", "Margin", "margin"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Users(Assembly assembly, string? outside = null) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(Structures).GetTypes()
            .Where(t => outside is null || !(t.Namespace ?? string.Empty).StartsWith(outside, StringComparison.Ordinal))
            .Select(t => (t.DeclaringType ?? t).Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_structure_types_are_pure_they_do_no_io_read_no_clock_and_draw_no_random_number_and_name_no_application_or_decision_code()
    {
        string[] banned =
        [
            "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Threading.Tasks", "System.Reflection", "System.Text.Json", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment",
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Domain.Options.Quality", "XDrishti.Domain.Options.Evaluation", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution",
        ];

        Domain.GetTypes().Count(t => t.Namespace == Structures).ShouldBeGreaterThan(8);
        var result = Types.InAssembly(Domain).That().ResideInNamespace(Structures).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var file in Directory.GetFiles(Src("XDrishti.Domain", "Options", "Structures"), "*.cs"))
        {
            Regex.IsMatch(File.ReadAllText(file), @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|Environment\.|File\.|Stopwatch)", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_structure_type_is_public_and_sealed_or_static_and_declared_in_a_file_of_its_name()
    {
        var dir = Src("XDrishti.Domain", "Options", "Structures");
        foreach (var type in Domain.GetTypes().Where(t => t.Namespace == Structures && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal)))
        {
            type.IsPublic.ShouldBeTrue(type.Name);
            (type.IsSealed || type.IsEnum).ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }
    }

    [Fact]
    public void The_exact_set_of_types_that_name_a_structure_type_is_the_two_use_cases_the_store_port_and_the_primary_profiles()
    {
        Users(Application).ShouldBe(["IExpressionTradeStore", "RemarkStructureHandler", "SimulateCondorsHandler"]);
        Users(Infrastructure).ShouldBe(["ExpressionTradeStore"]);
        Users(Api).ShouldBeEmpty();
        Users(Domain, Structures).ShouldBe(["ExpressionProfiles"]); // the primary profile of each family (P14-10) is built from the structure parameters
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
    public void No_other_assembly_names_a_structure_type(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOn(Structures).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void No_decision_area_of_the_domain_or_the_application_names_a_structure_type()
    {
        foreach (var area in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation" })
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOn(Structures).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in new[] { "XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live", "XDrishti.Application.Learning" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOn(Structures).GetResult().IsSuccessful.ShouldBeTrue(area);
        }
    }

    [Fact]
    public void The_structure_code_names_no_order_broker_margin_or_network_type_and_no_clock()
    {
        var files = Directory.EnumerateFiles(Src("XDrishti.Domain", "Options", "Structures"), "*.cs")
            .Concat([Src("XDrishti.Application", "OptionExpression", "RemarkStructureHandler.cs"), Src("XDrishti.Application", "OptionExpression", "SimulateCondorsHandler.cs"), Src("XDrishti.Application", "OptionExpression", "RemarkDispatchHandler.cs"), Src("XDrishti.Api", "Cli", "OptionsCondorsCommand.cs")]);
        foreach (var file in files)
        {
            var code = Regex.Replace(Regex.Replace(File.ReadAllText(file), "/\\*.*?\\*/", string.Empty, RegexOptions.Singleline, ScanRegex.Timeout), "//.*$|///.*$", string.Empty, RegexOptions.Multiline, ScanRegex.Timeout);
            foreach (var word in OrderWords)
            {
                code.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void A_short_leg_is_only_ever_built_in_a_pair_with_a_long_leg_beyond_it()
    {
        // the one type that builds legs lists them in pairs: a short leg is always followed (or preceded) by a long leg of the same kind; there is no constructor of a lone short leg.
        var text = File.ReadAllText(Src("XDrishti.Domain", "Options", "Structures", "StructureRemark.cs"));

        Regex.Count(text, @"new\(false,", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBe(Regex.Count(text, @"new\(true,", RegexOptions.CultureInvariant, ScanRegex.Timeout), "every short leg has a long leg built beside it");
        text.ShouldContain("There is no naked short leg");
    }

    [Fact]
    public void The_pair_and_pairs_code_stay_apart_from_the_structures_and_the_family_list_is_not_the_p15_phase_check_list()
    {
        XDrishti.Domain.Learning.Extensions.ExtensionFamilies.All.ShouldNotContain(f => f.StartsWith("expr:", StringComparison.Ordinal));
        XDrishti.Domain.Learning.Extensions.PairFamilies.All.ShouldNotContain(f => f.StartsWith("expr:", StringComparison.Ordinal));
        XDrishti.Domain.Learning.Extensions.ExpressionFamilies.All.Count.ShouldBe(4);
    }
}
