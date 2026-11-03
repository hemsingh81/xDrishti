using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-08 criteria 1, 4 and 9: the announcement flag reads untrusted text, so its reach is pinned. The flagger, its handlers and the set-D runner reference no tool registry, no MCP client, no HTTP client, no file or process type and no execution, planning or broker type; the
/// model is reached only through <c>IChatModel</c> (the P9-01 chat client). The extraction table, the answer type, the two ports and <c>llm-1</c> are named only by the exact set of types that build, store and report them: no selection, planning, checklist, simulation, strategy,
/// scoring or execution code names them. The table is written by one file.
/// </summary>
public sealed class AnnouncementExtractionArchitectureTests
{
    private const string ExtractionNamespace = "XDrishti.Domain.Filings.Extraction";
    private const string Flagger = "XDrishti.Application.Abstractions.Filings.IAnnouncementFlagger";
    private const string Store = "XDrishti.Application.Abstractions.Filings.IAnnouncementExtractionStore";
    private const string Llm = "XDrishti.Domain.Learning.Extensions.LlmFeatureCatalog";

    private static readonly string[] Names = [ExtractionNamespace, Flagger, Store, Llm];

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Users(Assembly assembly, params string[] own) =>
    [
        .. Types.InAssembly(assembly).That().HaveDependencyOnAny(Names).GetTypes()
            .Where(t => !own.Any(n => (t.Namespace ?? string.Empty).StartsWith(n, StringComparison.Ordinal)))
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal),
    ];

    [Theory]
    [InlineData("XDrishti.Application", "AnnouncementSetRunner")]
    [InlineData("XDrishti.Application", "ExtractAnnouncementsHandler")]
    [InlineData("XDrishti.Application", "GetExtractionCoverageHandler")]
    [InlineData("XDrishti.Application", "EvalFlagGradeSource")]
    [InlineData("XDrishti.Infrastructure", "AnnouncementFlagger")]
    [InlineData("XDrishti.Infrastructure", "AnnouncementExtractionStore")]
    public void The_flagger_the_handlers_the_runner_and_the_stores_reference_no_tool_registry_mcp_http_file_process_execution_planning_or_broker_type(string assemblyName, string typeName)
    {
        string[] banned =
        [
            "XDrishti.Application.Abstractions.Ai.IToolCatalog", "XDrishti.Application.Abstractions.Ai.IEvalToolCatalog", "XDrishti.Application.Abstractions.Ai.IAiSqlRunner", "XDrishti.Application.Abstractions.Ai.IAiDataReader", "XDrishti.Mcp", "ModelContextProtocol",
            "System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "System.Diagnostics.Process", "System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.IO.StreamWriter",
            "XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.Planning", "XDrishti.Application.LiveTrading", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Infrastructure.Brokers",
        ];

        var result = Types.InAssembly(Assembly.Load(new AssemblyName(assemblyName))).That().HaveName(typeName).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(typeName + " violates the reach of the flag: " + string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Assembly.Load(new AssemblyName(assemblyName))).That().HaveName(typeName).GetTypes().ShouldNotBeEmpty(typeName + " exists");
    }

    [Fact]
    public void The_flagger_offers_no_tool_and_reaches_the_model_only_through_the_chat_model_port()
    {
        var flagger = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", "AnnouncementFlagger.cs"));

        flagger.ShouldContain("IChatModel model");
        flagger.ShouldContain("new([new ModelMessage(ModelRole.System, PromptRegistry.AnnouncementFlag.Text), new ModelMessage(ModelRole.User, userMessage)], [], MaxOutputTokens, 0f, Seed)");
        flagger.ShouldNotContain("HttpClient");
        flagger.ShouldNotContain("IChatClient");
        flagger.ShouldNotContain("ToolDescriptor");
        flagger.ShouldNotContain("IToolCatalog");
        Types.InAssembly(Infrastructure).That().HaveName("AnnouncementFlagger").ShouldNot().HaveDependencyOn("Microsoft.Extensions.AI").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_exact_set_of_domain_types_outside_the_extraction_folder_that_name_it_is_empty()
    {
        Users(Domain, ExtractionNamespace).ShouldBeEmpty();
    }

    [Fact]
    public void The_exact_set_of_application_types_that_name_the_answer_the_two_ports_or_llm_1_is_the_flag_record_the_handler_the_runner_the_coverage_and_the_new_extraction_record()
    {
        Users(Application, ExtractionNamespace).ShouldBe(
        [
            "AnnouncementFlag", "AnnouncementSetRunner", "ExtractAnnouncementsHandler", "GetExtractionCoverageHandler", "NewExtraction",
        ]);
    }

    [Fact]
    public void The_exact_set_of_infrastructure_types_that_name_them_is_the_flagger_the_store_and_the_wiring()
    {
        Users(Infrastructure, ExtractionNamespace).ShouldBe(["AnnouncementExtractionStore", "AnnouncementFlagger", "DependencyInjection"]);
    }

    [Theory]
    [InlineData("XDrishti.Execution")]
    [InlineData("XDrishti.Simulation")]
    [InlineData("XDrishti.Strategies")]
    [InlineData("XDrishti.Strategies.File")]
    [InlineData("XDrishti.Indicators")]
    [InlineData("XDrishti.Learning.MlNet")]
    [InlineData("XDrishti.Mcp")]
    [InlineData("XDrishti.Worker")]
    [InlineData("XDrishti.Feed")]
    [InlineData("XDrishti.SimExport")]
    [InlineData("XDrishti.SimReader")]
    public void No_other_assembly_names_the_answer_the_two_ports_or_llm_1(string name)
    {
        Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue(name);
    }

    [Fact]
    public void The_api_names_only_the_coverage_view_and_the_verb_and_never_the_answer_or_the_ports()
    {
        var api = Assembly.Load(new AssemblyName("XDrishti.Api"));

        Types.InAssembly(api).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_decision_areas_the_scoring_path_and_the_selection_plan_and_checklist_code_name_none_of_them()
    {
        string[] domainAreas = ["XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper", "XDrishti.Domain.Execution", "XDrishti.Domain.Allocation", "XDrishti.Domain.Research", "XDrishti.Domain.Learning"];
        string[] applicationAreas = ["XDrishti.Application.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Paper", "XDrishti.Application.Live", "XDrishti.Application.Lab", "XDrishti.Application.Learning", "XDrishti.Application.Portfolio"];
        foreach (var area in domainAreas)
        {
            Types.InAssembly(Domain).That().ResideInNamespaceStartingWith(area).And().DoNotHaveName("LlmFeatureCatalog").ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var area in applicationAreas)
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        Types.InAssembly(Application).That().HaveNameMatching("ScoreWithModelHandler|ModelRegistry|ChampionRule|ScorerLoader|FlagEvaluation").ShouldNot().HaveDependencyOnAny(Names).GetResult().IsSuccessful.ShouldBeTrue("the scoring path and protocol L");
    }

    [Fact]
    public void Nothing_reads_llm_1_but_its_own_definition_and_the_group_is_not_returned_by_the_extractable_catalog()
    {
        Types.InAssembly(Domain).That().DoNotHaveName("LlmFeatureCatalog").ShouldNot().HaveDependencyOn(Llm).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).ShouldNot().HaveDependencyOn(Llm).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn(Llm).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Exactly_the_migration_and_the_store_name_the_extractions_table_and_the_store_only_inserts_and_selects()
    {
        var files = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !Path.GetFileName(f).EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"(FROM|INTO|JOIN|TABLE|UPDATE)\s+filings\.extractions", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        files.ShouldBe(["20261102234713_AddFilingExtractions.cs", "AnnouncementExtractionStore.cs"]);
        var store = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", "AnnouncementExtractionStore.cs"));
        foreach (var word in new[] { "UPDATE ", "DELETE ", "TRUNCATE", "DROP " })
        {
            store.ShouldNotContain(word, Case.Sensitive);
        }
    }

    [Fact]
    public void The_migration_grants_the_runtime_role_insert_only_per_table_and_names_no_other_role_and_has_a_down()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "Persistence", "Migrations", "20261102234713_AddFilingExtractions.cs"));

        text.ShouldContain("GrantTableToAppRole(Schemas.Filings, \"extractions\")");
        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Filings, \"extractions\")");
        text.ShouldContain("DROP TABLE IF EXISTS filings.extractions");
        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldNotContain("xd_exec", Case.Sensitive);
        text.ShouldNotContain("GRANT ", Case.Sensitive);
        text.ShouldNotContain("SECURITY DEFINER");
    }

    [Fact]
    public void The_prompt_text_and_the_hostile_set_are_in_the_shipped_source_and_the_labelled_text_is_not_under_eval()
    {
        File.Exists(Src("XDrishti.Application", "Ai", "Prompts", "announcement-flag.v1.txt")).ShouldBeTrue();
        File.Exists(Src("XDrishti.Domain", "Filings", "Extraction", "HostileAnnouncements.cs")).ShouldBeTrue();
    }
}
