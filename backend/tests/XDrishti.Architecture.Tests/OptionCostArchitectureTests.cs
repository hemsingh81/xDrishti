using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Domain.Options.Costs;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-07 criteria 7 and 9 (ADR 0194): the option cost and liquidity model is on no decision, live or execution path; its Domain types are pure; its one reader of <c>options.chain_snapshots</c> selects the instant, expiry, strike, the underlying's
/// price, the bid and the ask over one <c>(underlying_id, ts)</c> range and nothing else; its two tables are named by its store alone; and no rate, tax or brokerage figure is in <c>src/</c> or in a shipped setting (the owner's lines are entered with
/// <c>cost-sheet load</c>; the assumed half-spread is the owner's setting and is empty by default).
/// </summary>
public sealed class OptionCostArchitectureTests
{
    private static readonly string[] CostNamespaces =
        ["XDrishti.Domain.Options.Costs", "XDrishti.Application.OptionCosts", "XDrishti.Application.Abstractions.OptionCosts", "XDrishti.Infrastructure.OptionCosts"];

    private static readonly string[] Assemblies =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain", "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Learning.MlNet", "XDrishti.Execution", "XDrishti.Hosting", "XDrishti.Simulation", "XDrishti.Strategies",
        "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader",
    ];

    /// <summary>The only types outside the cost namespaces that name a cost type: the DI roots, the one verb and (P14-08, ADR 0197, by name) the re-mark use case, and (P14-16, ADR 0201, by name) the archive summary, which only reads the stored model's session count and dates; the re-mark use case asks for the spread model, the liquidity thresholds and the assumption; the re-mark's Domain types are exempted by namespace below.</summary>
    private const string MayReference = "^(DependencyInjection|OptionsSpreadModelCommand|RemarkRunHandler|RemarkStructureHandler|SimulateCondorsHandler|EvaluateExpressionHandler|GetOptionArchiveSummaryHandler|SpreadModelIds)$";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static List<string> SourceFiles() => [.. Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static string WithoutComments(string text) =>
        Regex.Replace(Regex.Replace(text, "/\\*.*?\\*/", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(5)), "//.*$", string.Empty, RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    private static string WithoutCommentsAndStrings(string text) => Regex.Replace(WithoutComments(text), "\"(\\\\.|[^\"\\\\])*\"", "\"\"", RegexOptions.None, TimeSpan.FromSeconds(5));

    [Fact]
    public void No_decision_live_recorder_or_execution_code_and_no_other_assembly_references_a_cost_type()
    {
        foreach (var name in Assemblies)
        {
            var result = Types.InAssembly(Assembly.Load(new AssemblyName(name))).That()
                .DoNotResideInNamespaceStartingWith("XDrishti.Domain.Options.Costs").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.OptionCosts")
                .And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Abstractions.OptionCosts").And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionCosts")
                .And().DoNotResideInNamespaceStartingWith("XDrishti.Domain.Options.Remark") // P14-08 (ADR 0197): the option re-mark is the one Domain area that costs a leg, by exact namespace
                .And().DoNotResideInNamespaceStartingWith("XDrishti.Domain.Options.Structures") // P14-10 (ADR 0200): the structures cost every leg, by exact namespace
                .And().DoNotHaveNameMatching(MayReference)
                .ShouldNot().HaveDependencyOnAny(CostNamespaces).GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_rule_is_not_vacuous_the_namespaces_hold_types_and_the_wiring_and_the_verb_do_reference_them()
    {
        Domain.GetTypes().Count(t => t.Namespace == "XDrishti.Domain.Options.Costs").ShouldBeGreaterThan(10);
        Application.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Application.OptionCosts");
        Application.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Application.Abstractions.OptionCosts");
        Infrastructure.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Infrastructure.OptionCosts");
        Types.InAssembly(Application).That().HaveNameMatching("^DependencyInjection$").Should().HaveDependencyOn("XDrishti.Application.OptionCosts").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Infrastructure).That().HaveNameMatching("^DependencyInjection$").Should().HaveDependencyOn("XDrishti.Infrastructure.OptionCosts").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Assembly.Load(new AssemblyName("XDrishti.Api"))).That().HaveName("OptionsSpreadModelCommand").Should().HaveDependencyOn("XDrishti.Application.OptionCosts").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_domain_cost_types_are_pure_they_do_no_io_read_no_clock_and_draw_no_random_number()
    {
        string[] banned =
        [
            "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Stopwatch", "System.Security", "System.Reflection", "System.Text.Json", "Microsoft", "Npgsql", "System.TimeProvider", "System.Random", "System.Environment",
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain.Options.Pricing", "XDrishti.Domain.Options.Quality", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Checklist", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Paper",
        ];

        var result = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Options.Costs").ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_cost_sheet_and_the_research_types_do_not_depend_on_the_option_cost_types_the_dependency_runs_one_way()
    {
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Research").ShouldNot().HaveDependencyOn("XDrishti.Domain.Options").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_spread_reader_port_has_one_read_method_returning_the_bid_ask_and_bucket_keys_only()
    {
        typeof(IChainSpreadReader).GetMethods().Select(m => m.Name).ShouldBe(["QuotesAsync"]);
        typeof(IChainSpreadReader).GetProperties().ShouldBeEmpty();
        typeof(SpreadQuote).GetProperties().Where(p => p.DeclaringType == typeof(SpreadQuote) && p.Name != "EqualityContract").Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["AskPx", "BidPx", "Expiry", "StrikePx", "Ts", "TwoSided", "UnderlyingPx"]);
    }

    [Fact]
    public void The_spread_reader_selects_six_columns_over_one_underlying_and_time_range_and_touches_no_other_table_and_no_write()
    {
        var text = Source("XDrishti.Infrastructure", "OptionCosts", "ChainSpreadReader.cs");
        var sql = WithoutComments(text);
        var code = WithoutCommentsAndStrings(text);

        text.ShouldContain("SELECT ts, expiry, strike_px, underlying_px, bid_px, ask_px");
        text.ShouldContain("FROM options.chain_snapshots");
        text.ShouldContain("WHERE underlying_id = @u AND ts >= @from AND ts < @to");
        Regex.Count(text, "FROM\\s+\\w+\\.\\w+", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBe(1);
        foreach (var column in new[] { "iv", "delta", "gamma", "vega", "oi", "volume", "flags", "security_id", "side", "bid_qty", "ask_qty" })
        {
            Regex.IsMatch(sql, "\\b" + column + "\\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)).ShouldBeFalse(column);
        }

        foreach (var write in new[] { "INSERT ", "UPDATE ", "DELETE ", "TRUNCATE ", "COPY ", "ALTER ", "DROP ", "CREATE ", "ExecuteNonQuery", "BeginBinaryImport", "quality_", "market.", "execution.", "trading." })
        {
            text.ShouldNotContain(write, Case.Sensitive);
        }

        code.ShouldNotContain("TimeProvider");
    }

    [Fact]
    public void Only_the_cost_handler_and_its_reader_and_wiring_name_the_spread_reader_and_the_handler_calls_it_only_for_usable_sessions()
    {
        foreach (var name in Assemblies)
        {
            var result = Types.InAssembly(Assembly.Load(new AssemblyName(name))).That().DoNotHaveName("BuildSpreadModelHandler").And().DoNotHaveName("ChainSpreadReader").And().DoNotHaveNameMatching("^DependencyInjection$")
                .And().DoNotHaveNameMatching("^IChainSpreadReader$").ShouldNot().HaveDependencyOn("XDrishti.Application.Abstractions.OptionCosts.IChainSpreadReader").GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        var handler = Source("XDrishti.Application", "OptionCosts", "BuildSpreadModelHandler.cs");
        Regex.Count(handler, "quotes\\.QuotesAsync\\(", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBe(1);
        handler.IndexOf("SessionUsability.IsUsable", StringComparison.Ordinal).ShouldBeLessThan(handler.IndexOf("quotes.QuotesAsync(", StringComparison.Ordinal));
        handler.ShouldContain("foreach (var session in usable)");
    }

    [Fact]
    public void Only_the_spread_store_names_the_two_model_tables_and_only_with_select_and_insert()
    {
        var files = SourceFiles();
        string[] Naming(string table) => [.. files.Where(f => Regex.IsMatch(File.ReadAllText(f), "(FROM|INTO|UPDATE|JOIN|DELETE)\\s+research\\." + table + "\\b", RegexOptions.None, TimeSpan.FromSeconds(5))).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

        Naming("option_spread_models").ShouldBe(["SpreadModelStore.cs"]);
        Naming("option_spread_buckets").ShouldBe(["SpreadModelStore.cs"]);
        var store = Source("XDrishti.Infrastructure", "OptionCosts", "SpreadModelStore.cs");
        foreach (var banned in new[] { "UPDATE ", "DELETE ", "TRUNCATE ", "options.", "market.", "execution.", "trading." })
        {
            store.ShouldNotContain(banned, Case.Sensitive);
        }

        typeof(ISpreadModelStore).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["AppendAsync", "ExistsAsync", "LatestAsync"]);
    }

    [Fact]
    public void No_option_or_futures_code_path_holds_a_rate_or_a_tax_figure()
    {
        // The option cost types and the settings hold no fractional number but the percent-to-fraction scale and the rounding of rupees; the segment code paths of the sheet hold no rate literal at all.
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "src", "XDrishti.Domain", "Options", "Costs"), "*.cs"))
        {
            var code = WithoutCommentsAndStrings(File.ReadAllText(file));
            Regex.IsMatch(code, "\\b\\d+\\.\\d+[mMdDfF]?\\b", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBe(Path.GetFileName(file) == "HalfSpreadAccumulator.cs", Path.GetFileName(file)); // the accumulator's 100.0 and 2.0 only
        }

        var accumulator = WithoutCommentsAndStrings(Source("XDrishti.Domain", "Options", "Costs", "HalfSpreadAccumulator.cs"));
        Regex.Matches(accumulator, "\\b\\d+\\.\\d+[mMdDfF]?\\b", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ShouldBe(["100.0", "2.0"]);

        // a sheet is built from the owner's text; no source file builds rates from literals
        SourceFiles().Where(f => Regex.IsMatch(File.ReadAllText(f), "ChargeRates\\.Create\\(", RegexOptions.None, TimeSpan.FromSeconds(5))).Select(Path.GetFileName).ShouldBe(["CostSheetJson.cs"]);
        Regex.IsMatch(Source("XDrishti.Domain", "Research", "CostSheetJson.cs"), "ChargeRates\\.Create\\([^)]*\\d+(\\.\\d+)?m", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBeFalse();

        // the settings: the assumed half-spread has no default and no shipped value; the two least counts are not rates
        var options = Source("XDrishti.Infrastructure", "OptionCosts", "OptionCostsOptions.cs");
        options.ShouldContain("Dictionary<string, decimal> AssumedHalfSpreadPct { get; set; } = [];");
        foreach (var settings in Directory.GetFiles(Path.Combine(Root, "src"), "appsettings*.json", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            File.ReadAllText(settings).ShouldNotContain("AssumedHalfSpreadPct");
            File.ReadAllText(settings).ShouldNotContain("\"Costs\"");
        }
    }
}
