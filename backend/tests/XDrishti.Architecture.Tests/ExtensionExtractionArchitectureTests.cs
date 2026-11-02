using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-05 criterion 8 (ADR 0185): the extension features of <c>ev-1</c> and <c>fu-1</c> are research only. The extractor and its handler are named by nothing but the wiring (and each other); the extraction table is named by the migration, its store and its port alone and is
/// written insert-only, per table, with nothing for any other role; Indicators knows no filings type; no decision area reads the extractor, the record or the port.
/// </summary>
public sealed class ExtensionExtractionArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Indicators = Assembly.Load(new AssemblyName("XDrishti.Indicators"));

    private static readonly string[] Names =
    [
        "XDrishti.Application.Learning.ExtensionFeatureExtractor", "XDrishti.Application.Learning.ExtractExtensionFeaturesHandler", "XDrishti.Application.Abstractions.Learning.IExtensionExtractionStore",
        "XDrishti.Domain.Learning.Extensions.ExtensionExtractionRecord", "XDrishti.Domain.Learning.Extensions.EventFeatures", "XDrishti.Domain.Learning.Extensions.FundamentalFeatures",
    ];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    [Fact]
    public void Nothing_but_the_wiring_and_the_handler_names_the_extractor_and_only_the_handler_and_the_p15_10_training_source_name_the_port_and_the_record()
    {
        List<string> Users(string name) =>
        [
            .. Types.InAssembly(Application).That().HaveDependencyOn(name).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal),
        ];

        Users("XDrishti.Application.Learning.ExtensionFeatureExtractor").ShouldBe(["DependencyInjection", "ExtensionFeatureExtractor", "ExtractExtensionFeaturesHandler", "SurpriseInputsReader"]);   // P15-09: the surprise reader of a run reuses the extractor at the close of each reaction session
        Users("XDrishti.Application.Learning.ExtractExtensionFeaturesHandler").ShouldBe(["DependencyInjection", "ExtractExtensionFeaturesHandler"]);
        Users("XDrishti.Application.Abstractions.Learning.IExtensionExtractionStore").ShouldBe(["ExtensionTrainingSource", "ExtractExtensionFeaturesHandler"]);   // P15-10: the training source reads the record per run for the evaluation gate
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
    public void No_other_assembly_names_the_extractor_the_record_the_port_or_the_two_feature_functions(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void The_decision_areas_name_none_of_them_and_the_verb_names_only_the_command_and_its_outcome()
    {
        string[] domainAreas = ["XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation"];
        string[] applicationAreas = ["XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live"];
        foreach (var area in domainAreas)
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in applicationAreas)
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        Types.InAssembly(Assembly.Load(new AssemblyName("XDrishti.Api"))).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue("the verb names the command and its outcome only");
    }

    [Fact]
    public void The_extractor_reads_day_bars_through_the_query_port_and_never_the_minute_bar_store()
    {
        Types.InAssembly(Application).That().HaveName("ExtensionFeatureExtractor").ShouldNot().HaveDependencyOn("XDrishti.Application.Abstractions.Market.Bars.IBarStore").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("ExtensionFeatureExtractor").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Market.Bars.IBarQueries").GetResult().IsSuccessful.ShouldBeTrue();
        File.ReadAllText(Src("XDrishti.Application", "Learning", "ExtensionFeatureExtractor.cs")).ShouldNotContain("SimulationBounds");
    }

    [Fact]
    public void The_indicator_library_knows_no_filings_type_and_the_frame_options_carry_neutral_numbers_only()
    {
        Types.InAssembly(Indicators).ShouldNot().HaveDependencyOnAny("XDrishti.Domain.Filings", "XDrishti.Domain.Learning").GetResult().IsSuccessful.ShouldBeTrue();
        typeof(XDrishti.Indicators.FrameOptions).GetProperty(nameof(XDrishti.Indicators.FrameOptions.ResultsDays))!.PropertyType
            .ShouldBe(typeof(IReadOnlyDictionary<DateOnly, XDrishti.Indicators.ResultsDayValues>));
    }

    [Fact]
    public void Exactly_the_migration_the_two_stores_and_their_ports_name_the_extraction_table()
    {
        var files = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !Path.GetFileName(f).EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("extension_extractions", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        files.ShouldBe(["20261102140834_AddExtensionExtractions.cs", "ExtensionExtractionStore.cs", "ExtensionFeatureStore.cs", "IExtensionExtractionStore.cs"]);
    }

    [Fact]
    public void The_extraction_store_only_selects_the_feature_store_only_inserts_and_the_migration_grants_the_runtime_role_per_table_and_nobody_else()
    {
        foreach (var file in new[] { "ExtensionExtractionStore.cs", "ExtensionFeatureStore.cs" })
        {
            var store = File.ReadAllText(Src("XDrishti.Infrastructure", "Learning", file));
            foreach (var word in new[] { "UPDATE ", "DELETE ", "TRUNCATE", "DROP " })
            {
                store.ShouldNotContain(word, Case.Sensitive, file);
            }
        }

        File.ReadAllText(Src("XDrishti.Infrastructure", "Learning", "ExtensionExtractionStore.cs")).ShouldNotContain("INSERT", Case.Sensitive);

        var migration = File.ReadAllText(Src("XDrishti.Infrastructure", "Persistence", "Migrations", "20261102140834_AddExtensionExtractions.cs"));
        migration.ShouldNotContain("GrantSchemaUsageToAppRole");
        migration.ShouldNotContain("xd_exec");
        migration.ShouldNotContain("xd_mcp");
        migration.ShouldNotContain("SECURITY DEFINER");
        migration.ShouldNotContain("CREATE FUNCTION", Case.Insensitive);
        migration.ShouldNotContain("CREATE TRIGGER", Case.Insensitive);
        migration.ShouldContain("GrantTableToAppRole(Schemas.Research, \"extension_extractions\")");
        migration.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Research, \"extension_extractions\")");
        migration.ShouldContain("DROP TABLE IF EXISTS research.extension_extractions");
    }

    [Fact]
    public void The_feature_functions_read_no_clock_no_random_and_no_io()
    {
        foreach (var file in new[] { "EventFeatures.cs", "FundamentalFeatures.cs", "SurpriseMath.cs", "EpsAdjustment.cs", "QuarterEnds.cs", "ExtensionNanCounts.cs" })
        {
            var text = File.ReadAllText(Src("XDrishti.Domain", "Learning", "Extensions", file));
            foreach (var word in new[] { "DateTime.Now", "UtcNow", "TimeProvider", "Random", "System.IO", "HttpClient", "Npgsql" })
            {
                text.ShouldNotContain(word, Case.Sensitive, file);
            }
        }
    }
}
