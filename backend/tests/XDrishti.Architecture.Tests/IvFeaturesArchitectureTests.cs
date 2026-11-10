using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-11 criterion 7 (ADR 0199): the <c>iv-1</c> features are research only. Their pure types are pure; who may name the catalog, the pure functions, the extractor and its port is an exact set (the verbs, the route, the use cases of P15-05 and P15-10, the wiring); nothing in selection,
/// planning, paper, live trading or the execution service names them; the pure types do not name the pricing core or any archive reader; the extractor reads through ports and filters by underlying, key and range in the query; no migration, no new table, no scoring switch.
/// </summary>
public sealed class IvFeaturesArchitectureTests
{
    private const string Iv = "XDrishti.Domain.Options.Iv";
    private const string Catalog = "XDrishti.Domain.Learning.Extensions.IvFeatureCatalog";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = Assembly.Load(new AssemblyName("XDrishti.Api"));

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Users(Assembly assembly, string dependency, string? outside = null) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(dependency).GetTypes()
            .Where(t => outside is null || !(t.Namespace ?? string.Empty).StartsWith(outside, StringComparison.Ordinal))
            .Select(t => (t.DeclaringType ?? t).Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_iv_types_are_pure_do_no_io_read_no_clock_draw_no_random_number_and_name_no_pricing_application_or_decision_code()
    {
        string[] banned =
        [
            "System.IO", "System.Net", "System.Data", "System.Diagnostics.Stopwatch", "System.Threading.Tasks", "System.Reflection", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment",
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Domain.Options.Pricing", "XDrishti.Domain.Options.Quality", "XDrishti.Domain.Options.Evaluation", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning",
            "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution",
        ];

        Domain.GetTypes().Count(t => t.Namespace == Iv).ShouldBeGreaterThan(8);
        var result = Types.InAssembly(Domain).That().ResideInNamespace(Iv).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var file in Directory.GetFiles(Src("XDrishti.Domain", "Options", "Iv"), "*.cs").Append(Src("XDrishti.Domain", "Learning", "Extensions", "IvFeatureCatalog.cs")).Append(Src("XDrishti.Domain", "Learning", "Extensions", "IvSourceNotes.cs")))
        {
            Regex.IsMatch(File.ReadAllText(file), @"\b(DateTime(Offset)?\.(UtcNow|Now)|TimeProvider|Random\.Shared|new Random\(|Environment\.|File\.|Stopwatch)", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBeFalse(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Each_iv_type_is_public_and_sealed_or_static_and_declared_in_a_file_of_its_name()
    {
        var dir = Src("XDrishti.Domain", "Options", "Iv");
        foreach (var type in Domain.GetTypes().Where(t => t.Namespace == Iv && !t.IsNested && !t.Name.Contains('<', StringComparison.Ordinal)))
        {
            type.IsPublic.ShouldBeTrue(type.Name);
            type.IsSealed.ShouldBeTrue(type.Name);
            File.Exists(Path.Combine(dir, type.Name + ".cs")).ShouldBeTrue(type.Name);
        }
    }

    [Fact]
    public void The_exact_set_of_types_that_name_an_iv_type_is_the_bar_port_the_extractor_and_the_bar_reader()
    {
        Users(Application, Iv).ShouldBe(["IRollingBarReader", "IvFeatureExtractor"]);
        Users(Infrastructure, Iv).ShouldBe(["RollingBarReader"]);
        Users(Api, Iv).ShouldBeEmpty();
        Users(Domain, Iv, Iv).ShouldBeEmpty();
    }

    [Fact]
    public void The_exact_set_of_types_that_name_the_catalog_and_the_extraction_notes_is_the_group_lookup_and_the_two_use_cases()
    {
        // the catalog's version and family are constants the compiler inlines, so only the types that read its names or group show up
        Users(Domain, Catalog).ShouldBe(["ExtensionCatalogs"]);
        Users(Application, Catalog).ShouldBeEmpty(); // the use cases read its version and family constants (inlined) and the group through ExtensionCatalogs
        Users(Application, "XDrishti.Domain.Learning.Extensions.IvSourceNotes").ShouldBe(["ComparisonEvaluation", "ExtractExtensionFeaturesHandler"]);
        Users(Infrastructure, Catalog).ShouldBeEmpty();
        Users(Api, Catalog).ShouldBeEmpty();
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
    public void No_other_assembly_names_an_iv_type_the_catalog_the_notes_the_extractor_or_its_port(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Iv, Catalog, "XDrishti.Domain.Learning.Extensions.IvSourceNotes", "XDrishti.Domain.Learning.Extensions.IvReasons", "XDrishti.Application.OptionIv", "XDrishti.Application.Abstractions.OptionIv").GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void No_decision_area_names_an_iv_type_the_catalog_the_extractor_or_its_port()
    {
        string[] names = [Iv, Catalog, "XDrishti.Domain.Learning.Extensions.IvSourceNotes", "XDrishti.Domain.Learning.Extensions.IvReasons", "XDrishti.Application.OptionIv", "XDrishti.Application.Abstractions.OptionIv"];
        foreach (var area in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation" })
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in new[] { "XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live", "XDrishti.Application.LearningPhase" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        Types.InAssembly(Domain).That().HaveName("EventsPhaseCheck").ShouldNot().HaveDependencyOnAny(names).GetResult().IsSuccessful.ShouldBeTrue("the P15 phase check never names an iv type");
        Types.InAssembly(Api).ShouldNot().HaveDependencyOnAny(Iv, "XDrishti.Application.OptionIv").GetResult().IsSuccessful.ShouldBeTrue("the verbs name the commands only");
    }

    [Fact]
    public void The_extractor_reads_through_ports_only_asks_for_one_underlying_at_a_time_and_names_no_order_or_clock()
    {
        var text = File.ReadAllText(Src("XDrishti.Application", "OptionIv", "IvFeatureExtractor.cs"));

        text.ShouldContain("IRollingBarReader");
        text.ShouldContain("IRollingQualityRuns");
        text.ShouldContain("HasSeriesAsync");
        foreach (var word in new[] { "Npgsql", "IBrokerOrders", "IOrderGateway", "PlaceOrder", "HttpClient", "DateTime.Now", "DateTime.UtcNow", "TimeProvider", "SimulationBounds", "IBarStore" })
        {
            text.ShouldNotContain(word, Case.Sensitive);
        }

        Types.InAssembly(Application).That().HaveName("IvFeatureExtractor").ShouldNot().HaveDependencyOnAny("Npgsql", "XDrishti.Infrastructure", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Abstractions.Execution").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_two_new_reads_are_select_only_and_filter_by_underlying_keys_and_range_in_the_query()
    {
        var quality = File.ReadAllText(Src("XDrishti.Infrastructure", "OptionQuality", "OptionQualityStore.cs"));
        var rolling = File.ReadAllText(Src("XDrishti.Infrastructure", "OptionExpression", "RollingBarReader.cs"));

        quality.ShouldContain("WHERE underlying_id = @u AND source = 'rolling' AND fetch_key = ANY(@keys) AND session_date BETWEEN @from AND @to");
        rolling.ShouldContain("underlying_id = @u AND expiry_flag = @flag AND expiry_code = ANY(@codes) AND interval_minutes = @interval AND atm_offset = 0");
        rolling.ShouldContain("atm_offset = ANY(@offsets)");
        foreach (var banned in new[] { "INSERT", "UPDATE ", "DELETE", "TRUNCATE", "DROP " })
        {
            rolling.ShouldNotContain(banned, Case.Sensitive);
        }

        typeof(XDrishti.Application.Abstractions.OptionQuality.IRollingQualityRuns).GetMethods().Select(m => m.Name).ShouldBe(["RollingResultsForKeysAsync"]);
    }

    [Fact]
    public void The_slice_adds_no_migration_and_no_scoring_switch()
    {
        Directory.EnumerateFiles(Src("XDrishti.Infrastructure", "Persistence", "Migrations"), "*.cs").Where(f => Path.GetFileName(f).Contains("Iv", StringComparison.Ordinal) && !Path.GetFileName(f).Contains("Divid", StringComparison.Ordinal)).ShouldBeEmpty();
        foreach (var file in Directory.EnumerateFiles(Src("XDrishti.Domain", "Options", "Iv"), "*.cs").Concat(Directory.EnumerateFiles(Src("XDrishti.Application", "OptionIv"), "*.cs")))
        {
            File.ReadAllText(file).ShouldNotContain("Setting", Case.Sensitive, Path.GetFileName(file));
        }
    }
}
