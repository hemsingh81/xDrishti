using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionQuality;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-04 criterion 10 (ADR 0178): the option-archive quality code is the one reader of the archive tables and is never on the trading path. The decision layers, the live feed, the recorders, the pure libraries and the execution service reference no quality
/// type; the quality code makes no network call, writes no archive table, touches no order, plan or live-session type and names no recorder type; only its reader names the archive tables and only its store names the result tables; the one route is a GET
/// that carries no price; the log carries counts only. The exact allow-lists below are the deliberate loosening of the P14-02 and P14-03 "no reader" tests: they name the quality reader and nothing else.
/// </summary>
public sealed class OptionQualityArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly string[] QualityNamespaces = ["XDrishti.Application.OptionQuality", "XDrishti.Application.Abstractions.OptionQuality", "XDrishti.Infrastructure.OptionQuality", "XDrishti.Domain.Options.Quality"];
    private static readonly string[] RecorderNamespaces = ["XDrishti.Application.OptionChain", "XDrishti.Application.Abstractions.OptionChain", "XDrishti.Infrastructure.OptionChain", "XDrishti.Application.OptionBackfill", "XDrishti.Application.Abstractions.OptionBackfill", "XDrishti.Infrastructure.OptionBackfill"];
    private static readonly string[] ArchiveTables = ["chain_snapshots", "chain_requests", "chain_sessions", "rolling_bars", "fetch_log"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static List<string> SourceFiles() => [.. Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    [Theory]
    [InlineData("XDrishti.Application.Planning")]
    [InlineData("XDrishti.Application.Learning")]
    [InlineData("XDrishti.Application.Portfolio")]
    [InlineData("XDrishti.Application.Execution")]
    [InlineData("XDrishti.Application.LiveTrading")]
    [InlineData("XDrishti.Application.Live")]
    [InlineData("XDrishti.Application.Selection")]
    [InlineData("XDrishti.Application.Research")]
    [InlineData("XDrishti.Application.Lab")]
    [InlineData("XDrishti.Application.Reporting")]
    [InlineData("XDrishti.Application.Paper")]
    [InlineData("XDrishti.Application.Depth")]
    [InlineData("XDrishti.Application.Ticks")]
    [InlineData("XDrishti.Application.OptionChain")]
    [InlineData("XDrishti.Application.OptionBackfill")]
    public void The_decision_live_and_recorder_code_references_no_quality_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(QualityNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// The simple names of the only types outside the quality namespaces that may name a quality type: the DI roots, the one route, the JSON context and (P14-07, ADR 0194, deliberately and by name) <c>BuildSpreadModelHandler</c>, which asks the quality
    /// results which chain sessions are usable before it reads any of them, and (P14-08, ADR 0197, by name) <c>RemarkRunHandler</c>, which asks the stored verdicts which rolling key-sessions are usable before it reads a bar as a price, and (P14-10, ADR 0200, by name) <c>RemarkStructureHandler</c>, <c>SimulateCondorsHandler</c> and their shared <c>RemarkShared</c>, which do the same for the structures, and (P14-11, ADR 0199, by name) <c>IvFeatureExtractor</c>, which does the same before it reads a bar as an IV or an open interest.
    /// </summary>
    private const string MayReference = "^(DependencyInjection|ApiJsonContext.*|OptionCoverageEndpoints|BuildSpreadModelHandler|RemarkRunHandler|RemarkStructureHandler|SimulateCondorsHandler|RemarkShared|EvaluateExpressionHandler|IvFeatureExtractor)$";

    [Fact]
    public void Only_the_quality_code_its_wiring_and_its_route_reference_a_quality_type_in_any_assembly_of_the_solution()
    {
        string[] names = ["XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain", "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Learning.MlNet", "XDrishti.Execution", "XDrishti.Hosting", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader"];
        foreach (var name in names)
        {
            var result = Types.InAssembly(Assembly.Load(new AssemblyName(name))).That()
                .DoNotResideInNamespaceStartingWith("XDrishti.Application.OptionQuality").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Abstractions.OptionQuality")
                .And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionQuality").And().DoNotResideInNamespaceStartingWith("XDrishti.Domain.Options.Quality")
                .And().DoNotHaveNameMatching(MayReference)
                .ShouldNot().HaveDependencyOnAny(QualityNamespaces).GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_rule_is_not_vacuous_the_quality_namespaces_hold_types_and_the_wiring_does_reference_them()
    {
        foreach (var ns in new[] { "XDrishti.Application.OptionQuality", "XDrishti.Application.Abstractions.OptionQuality" })
        {
            Application.GetTypes().ShouldContain(t => t.Namespace == ns);
        }

        Infrastructure.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Infrastructure.OptionQuality");
        Domain.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Domain.Options.Quality");
        Types.InAssembly(Application).That().HaveNameMatching("^DependencyInjection$").Should().HaveDependencyOn("XDrishti.Application.OptionQuality").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.OptionQuality").And().HaveName("OptionArchiveReader").Should().HaveDependencyOn("Npgsql").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_pure_libraries_and_the_execution_service_reference_no_quality_type()
    {
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Execution" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(QualityNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void The_quality_code_makes_no_network_call_touches_no_order_plan_or_live_type_and_names_no_recorder_type()
    {
        string[] banned =
        [
            "System.Net.Http", "System.Net.WebSockets", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Planning", "XDrishti.Application.Learning", "XDrishti.Application.Portfolio", "XDrishti.Application.Paper",
            "XDrishti.Application.Abstractions.Planning", "XDrishti.Application.Abstractions.Live", "XDrishti.Application.Live", "XDrishti.Application.Abstractions.Execution", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Abstractions.Depth",
            "XDrishti.Application.Abstractions.Ticks", "XDrishti.Application.Depth", "XDrishti.Application.Ticks", "XDrishti.Infrastructure.Brokers", .. RecorderNamespaces,
        ];
        foreach (var ns in new[] { "XDrishti.Application.OptionQuality", "XDrishti.Application.Abstractions.OptionQuality" })
        {
            var result = Types.InAssembly(Application).That().ResideInNamespace(ns).ShouldNot().HaveDependencyOnAny([.. banned, "Npgsql", "XDrishti.Infrastructure"]).GetResult();
            result.IsSuccessful.ShouldBeTrue(ns + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        var infrastructure = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.OptionQuality").ShouldNot().HaveDependencyOnAny(banned).GetResult();
        infrastructure.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", infrastructure.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Options.Quality").ShouldNot().HaveDependencyOnAny("Npgsql", "System.Net", "System.Data", "Microsoft.Extensions", "XDrishti.Application", "XDrishti.Infrastructure").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_quality_code_is_no_active_set_provider()
    {
        var provider = Application.GetType("XDrishti.Application.Live.IActiveSetProvider").ShouldNotBeNull();

        Application.GetTypes().Where(t => t.Namespace is "XDrishti.Application.OptionQuality").ShouldNotContain(t => provider.IsAssignableFrom(t));
    }

    [Fact]
    public void The_ports_are_pinned_the_archive_reader_has_only_reads_and_the_result_store_has_no_update_or_delete()
    {
        typeof(IOptionArchiveReader).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe([
            "ActiveDaysAsync", "ChainPointsAsync", "ChainRequestSummariesAsync", "ChainResultDatesAsync", "ChainSessionDatesAsync", "FirstChainSessionAsync", "PendingChainSessionsAsync", "PendingRollingWindowsAsync", "RecentChainUnderlyingsAsync", "RollingPointsAsync"]);
        typeof(IOptionReference).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["EpochsAsync", "FindUnderlyingAsync", "LotSizesAsync", "StrikeStepAsync", "SymbolsAsync"]);
        typeof(IOptionQualityStore).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["AddResultsAsync", "ChainResultsAsync", "FinishRunAsync", "RollingResultsAsync", "StartRunAsync"]);
        typeof(IOptionArchiveReader).GetProperties().ShouldBeEmpty();
    }

    [Fact]
    public void The_reader_and_the_reference_issue_only_selects_and_the_store_never_updates_deletes_or_touches_an_archive_table()
    {
        foreach (var file in new[] { "OptionArchiveReader.cs", "OptionReference.cs" })
        {
            var text = Source("XDrishti.Infrastructure", "OptionQuality", file);
            foreach (var write in new[] { "INSERT ", "UPDATE ", "DELETE ", "TRUNCATE ", "COPY ", "ALTER ", "DROP ", "CREATE ", "BeginBinaryImport", "ExecuteNonQuery", "execution.", "trading.", "UPDATE options", "DELETE FROM" })
            {
                text.ShouldNotContain(write, Case.Sensitive);
            }

            Regex.IsMatch(text, "\\binto\\s+options\\.", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5)).ShouldBeFalse();
        }

        var store = Source("XDrishti.Infrastructure", "OptionQuality", "OptionQualityStore.cs");
        string[] storeBanned = ["UPDATE ", "DELETE ", "TRUNCATE ", "market.", "execution.", "trading.", .. ArchiveTables];
        foreach (var banned in storeBanned)
        {
            store.ShouldNotContain(banned, Case.Sensitive);
        }

        store.ShouldContain("INSERT INTO options.quality_runs");
        store.ShouldContain("COPY options.quality_results");
    }

    [Fact]
    public void Only_the_quality_reader_and_the_recorders_own_stores_name_the_archive_tables_and_only_the_quality_store_and_reader_name_the_result_tables()
    {
        var files = SourceFiles();
        string[] Naming(string table) => [.. files.Where(f => Regex.IsMatch(File.ReadAllText(f), "(FROM|INTO|UPDATE|JOIN)\\s+options\\." + table + "\\b", RegexOptions.None, TimeSpan.FromSeconds(5))).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

        // Pinned per table: the recorder's own store and the quality reader, nothing else. The positive control is that each list names the quality reader (or, for chain_sessions, that it does not).
        // P14-07 (ADR 0194) adds exactly one file to chain_snapshots: ChainSpreadReader, the half-spread model's read of bid, ask and bucket keys of usable sessions.
        Naming("chain_snapshots").ShouldBe(["ChainSpreadReader.cs", "OptionArchiveReader.cs", "OptionChainStore.cs"]);
        Naming("chain_requests").ShouldBe(["OptionArchiveReader.cs", "OptionChainStore.cs"]);
        Naming("chain_sessions").ShouldBe(["OptionChainStore.cs"]);
        Naming("rolling_bars").ShouldBe(["OptionArchiveReader.cs", "RollingBarReader.cs", "RollingBarStore.cs"]); // P14-08 (ADR 0197) added the re-mark's RollingBarReader
        Naming("fetch_log").ShouldBe(["BackfillFetchLog.cs", "OptionArchiveReader.cs", "RollingBarReader.cs", "RollingBarStore.cs"]);
        Naming("quality_results").ShouldBe(["OptionArchiveReader.cs", "OptionQualityStore.cs"]);
        Naming("quality_runs").ShouldBe(["OptionQualityStore.cs"]);
    }

    [Fact]
    public void The_one_route_is_a_get_that_reaches_the_result_rows_only_through_its_handler_and_no_other_api_file_names_an_archive_or_result_table()
    {
        var api = Path.Combine(Root, "src", "XDrishti.Api");
        var routes = File.ReadAllText(Path.Combine(api, "Endpoints", "OptionCoverageEndpoints.cs"));
        routes.ShouldContain("MapGet(\"/coverage\"");
        routes.Split("group.Map").Length.ShouldBe(2, "exactly one route");
        routes.ShouldNotContain("IOptionQualityStore");
        routes.ShouldNotContain("IOptionArchiveReader");
        routes.ShouldNotContain("MapPost");
        routes.ShouldNotContain("MapPut");
        routes.ShouldNotContain("MapDelete");
        foreach (var name in new[] { "rolling_bars", "fetch_log", "chain_snapshots", "chain_requests", "quality_results", "quality_runs" })
        {
            Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)).ShouldAllBe(f => !File.ReadAllText(f).Contains(name, StringComparison.Ordinal), name);
        }
    }

    [Fact]
    public void The_log_ids_are_in_their_own_range_and_the_messages_carry_counts_only()
    {
        var methods = Application.GetType("XDrishti.Application.OptionQuality.OptionQualityLog")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        var messages = methods.SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).ToList();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(a => a.EventId >= 6971 && a.EventId <= 6980);
        foreach (var text in messages.Select(m => m.Message ?? string.Empty))
        {
            text.ShouldStartWith("options.quality.");
            foreach (var placeholder in new[] { "{Token", "{Client", "{Url", "{Uri", "{Symbol", "{Price", "{Security", "{Query", "{Body", "{Header", "{Scrip", "{UnderlyingId", "{UnderlyingSymbol", "{Iv", "{Delta" })
            {
                text.ShouldNotContain(placeholder, Case.Insensitive);
            }
        }

        foreach (var word in new[] { "run", "session", "done" })
        {
            messages.ShouldContain(a => (a.Message ?? string.Empty).StartsWith("options.quality." + word, StringComparison.Ordinal), word);
        }

        var ids = Application.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).Select(a => a.EventId).Where(id => id is >= 6901 and <= 6999).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void The_service_is_on_by_default_runs_in_the_api_host_and_cannot_be_started_by_the_recorders_run_now_refusal()
    {
        XDrishti.Application.OptionQuality.OptionArchiveQualityService.Definition.Defaults.Enabled.ShouldBeTrue();
        XDrishti.Application.OptionQuality.OptionArchiveQualityService.Definition.RunsAs.ShouldBe("xd-api");
        Source("XDrishti.Application", "DependencyInjection.cs").ShouldContain("IScheduledService, OptionArchiveQualityService");
        typeof(XDrishti.Application.OptionQuality.OptionArchiveQualityService).GetInterfaces().ShouldNotContain(i => i.Name == "IExternallyRunService");
    }
}
