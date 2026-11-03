using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P13-05 criterion 13: the feature-set comparison is research only. Who may name the extension types, the two ports and the two tables is an exact set, so a new reader (the P13-03/06 and P15-05/10 handlers) is a deliberate edit of
/// this file; no decision path reads them; the migration grants the runtime role insert and select on each table and nothing to any other role.
/// </summary>
public sealed class ExtensionComparisonArchitectureTests
{
    private const string ExtensionNamespace = "XDrishti.Domain.Learning.Extensions";

    private static readonly string[] Ports = ["XDrishti.Application.Abstractions.Learning.IExtensionFeatureStore", "XDrishti.Application.Abstractions.Learning.IExtensionEvaluationStore", "XDrishti.Application.Abstractions.Learning.IExtensionExtractionStore"];

    private static readonly string[] Dependencies = [ExtensionNamespace, .. Ports];

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    // Files that may name a table of the comparison: the migration, the two stores that read and write them, the two ports and the record, and nothing else (the later handlers add themselves here).
    private static readonly string[] TableFiles = ["20261102120000_AddExtensionTables.cs", "ExtensionEvaluationRecord.cs", "ExtensionEvaluationStore.cs", "ExtensionFeatureStore.cs", "IExtensionEvaluationStore.cs", "IExtensionFeatureStore.cs"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Users(Assembly assembly, params string[] own) =>
    [
        .. Types.InAssembly(assembly).That().HaveDependencyOnAny(Dependencies).GetTypes()
            .Where(t => !own.Any(n => (t.Namespace ?? string.Empty).StartsWith(n, StringComparison.Ordinal)))
            .Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void The_exact_set_of_domain_types_that_name_an_extension_type_is_the_training_data_builder_and_the_pairs_study()
    {
        Users(Domain, ExtensionNamespace).ShouldBe(["FormationResult", "PairFormationId", "PairQuarter", "PairTrial", "ProtocolS", "ProtocolSResult", "TrainingDataBuilder"]); // the first five: the pairs study (P14-13) builds on the extension record, the quarter code and the JSON writer
    }

    [Fact]
    public void The_exact_set_of_application_types_that_name_an_extension_type_or_port_is_the_ports_the_p15_05_extraction_and_the_p15_10_and_p14_13_evaluations()
    {
        Users(Application).ShouldBe(
        [
            "ComparisonEvaluation", "EvalFlagGradeSource", "EvaluatePairsHandler", "EvaluationWriter", "EventFilterVerdictReader", "EventsPhaseCheckView", "ExtensionEvaluationErrors", "ExtensionEvaluationMapper", "ExtensionFeatureExtractor", "ExtensionRunSource", "ExtensionTrainingSource", "ExtractExtensionFeaturesHandler",
            "FilterEvaluation", "FilterPopulation", "FilterPopulationSource", "FlagEvaluation", "GetEventsPhaseCheckHandler", "GetExtensionEvaluationHandler", "IExtensionEvaluationReader", "IExtensionEvaluationStore", "IExtensionExtractionStore", "IExtensionFeatureStore", "IExtensionTrainingSource",
            "IFlagGradeSource", "ListExtensionEvaluationsHandler", "PairConfig", "PeadEvaluation", "PeadEventSet", "PeadRunEventSource", "RunPairsHandler", "SurpriseInputsReader", "TrainingDataLoader",
        ]);
    }

    [Fact]
    public void The_exact_set_of_infrastructure_types_that_name_an_extension_type_or_port_is_the_three_stores_and_the_wiring()
    {
        Users(Infrastructure).ShouldBe(["DependencyInjection", "ExtensionEvaluationStore", "ExtensionExtractionStore", "ExtensionFeatureStore"]);
    }

    [Theory]
    [InlineData("XDrishti.Execution")]
    [InlineData("XDrishti.Simulation")]
    [InlineData("XDrishti.Strategies")]
    [InlineData("XDrishti.Strategies.File")]
    [InlineData("XDrishti.Indicators")]
    [InlineData("XDrishti.Learning.MlNet")]
    [InlineData("XDrishti.Mcp")]
    [InlineData("XDrishti.Api")]
    [InlineData("XDrishti.Worker")]
    [InlineData("XDrishti.Feed")]
    [InlineData("XDrishti.SimExport")]
    [InlineData("XDrishti.SimReader")]
    public void No_other_assembly_names_an_extension_type_or_port(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Dependencies).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void The_decision_areas_of_domain_and_application_and_the_scoring_path_name_none_of_them()
    {
        string[] domainAreas = ["XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation"];
        string[] applicationAreas = ["XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live"];
        foreach (var area in domainAreas)
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(Dependencies).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in applicationAreas)
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(Dependencies).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        Types.InAssembly(Application).That().HaveNameMatching("ScoreWithModelHandler|ModelRegistry|ChampionRule|ScorerLoader").ShouldNot().HaveDependencyOnAny(Dependencies).GetResult().IsSuccessful.ShouldBeTrue("the scoring path");
        Types.InAssembly(Infrastructure).That().HaveNameMatching("ModelRegistry|MlNetScorerLoader|ModelFiles").ShouldNot().HaveDependencyOnAny(Dependencies).GetResult().IsSuccessful.ShouldBeTrue("the champion path");
    }

    [Fact]
    public void Exactly_the_migration_the_stores_the_ports_and_the_record_name_a_table_of_the_comparison()
    {
        var files = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !Path.GetFileName(f).EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var text && (text.Contains("trade_features_ext", StringComparison.Ordinal) || text.Contains("extension_evaluations", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        files.ShouldBe([.. TableFiles.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_two_stores_only_insert_and_select()
    {
        foreach (var file in new[] { "ExtensionFeatureStore.cs", "ExtensionEvaluationStore.cs" })
        {
            var text = File.ReadAllText(Src("XDrishti.Infrastructure", "Learning", file));
            foreach (var word in new[] { "UPDATE ", "DELETE ", "TRUNCATE", "DROP " })
            {
                text.ShouldNotContain(word, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void The_migration_grants_the_runtime_role_per_table_and_nobody_else_and_has_no_function_or_trigger()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "Persistence", "Migrations", "20261102120000_AddExtensionTables.cs"));

        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldNotContain("xd_exec");
        text.ShouldNotContain("xd_mcp");
        text.ShouldNotContain("SECURITY DEFINER");
        text.ShouldNotContain("CREATE FUNCTION", Case.Insensitive);
        text.ShouldNotContain("CREATE TRIGGER", Case.Insensitive);
        if (text.Contains("FUNCTION", StringComparison.OrdinalIgnoreCase))
        {
            text.ShouldContain("SET search_path = pg_catalog, pg_temp");
        }

        foreach (var table in new[] { "trade_features_ext", "extension_evaluations" })
        {
            text.ShouldContain($"GrantTableToAppRole(Schemas.Research, \"{table}\")");
            text.ShouldContain($"MakeAppendOnlyForAppRole(Schemas.Research, \"{table}\")");
        }
    }

    [Fact]
    public void The_pure_core_reads_no_clock_no_shared_random_and_no_io_and_the_domain_does_not_reference_the_model_library()
    {
        foreach (var file in Directory.EnumerateFiles(Src("XDrishti.Domain", "Learning", "Extensions"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "DateTime.Now", "UtcNow", "TimeProvider", "Random.Shared", "new Random", "System.IO", "File.", "Directory.", "HttpClient", "Npgsql", "Task.", "async " })
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }

        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny("XDrishti.Learning.MlNet", "Microsoft.ML", "Npgsql").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Domain).That().ResideInNamespace(ExtensionNamespace).Should().NotHaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure").GetResult().IsSuccessful.ShouldBeTrue();
    }
}
