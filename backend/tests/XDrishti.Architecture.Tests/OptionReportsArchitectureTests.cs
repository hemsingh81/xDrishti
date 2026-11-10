using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionReports;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-16 criteria 2 and 5 (ADR 0201): the P14 reports are read only and show no series. The handlers hold read ports and nothing else; the reader port has exactly four reads and the reader issues SELECTs only, each bounded by a range, a run id or a limit and none selecting a price,
/// IV, Greek, bar, quote or return column; the response types carry exactly the pinned field names (counts, shares, dates, words, bounds); the routes are GETs; nothing in the reports names the execution service, selection, planning, a broker or the network.
/// </summary>
public sealed class OptionReportsArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] Namespaces = ["XDrishti.Application.OptionReports", "XDrishti.Application.Abstractions.OptionReports", "XDrishti.Infrastructure.OptionReports", "XDrishti.Domain.Options.Reports"];

    private static readonly string[] AllowedParameters =
    [
        "IOptionReportReader", "IBackfillFetchLog", "IOptionQualityStore", "IOptionReference", "ISpreadModelStore", "IExtensionEvaluationReader", "TimeProvider", "IQueryHandler`2",
    ];

    /// <summary>Every field name of every response type of the reports. A new name is a deliberate change of this list and of the review.</summary>
    private static readonly string[] AllowedNames =
    [
        "Archive", "Backfill", "BhThreshold", "Candidates", "CashRunKey", "Count", "CreatedAt", "Data", "DerivedExpiry", "Done", "Empty", "ExpiryDerivedShare", "Families", "Family", "FallbackShare", "Feasibility", "FirstDate", "FoldsEvaluable", "FoldsTotal", "ExpressionRuns", "From",
        "Gaps", "GivenUp", "HalfLives", "HoldOutRead", "Id", "Items", "Labels", "LastDate", "Limits", "LotFeasibility", "Max", "Median", "Min", "Missing", "NoData", "Outcome", "Outcomes", "Paired", "PairFormations", "ProfileHash", "Protocol", "Quarter", "Reason",
        "RecordedSessions", "RemarkVersion", "Selected", "Sessions", "Share", "SpreadFromRecentArchiveShare", "SpreadModelFirstSession", "SpreadModelLastSession", "SpreadModelSessions", "State", "Tested", "Text", "To", "Trades", "Truncated", "Trials", "Unavailable", "Unpaired", "Underlying",
        "Underlyings", "UsableSessions", "Value", "Verdict", "Verdicts", "WindowFrom", "WindowTo",
    ];

    private static readonly string[] BannedTokens = ["price", "iv", "greek", "greeks", "delta", "gamma", "vega", "theta", "bar", "bars", "quote", "bid", "ask", "premium", "strike", "close", "open", "high", "low", "ltp", "pnl", "return", "gross", "net", "tick", "ticks"];

    private static readonly string[] BannedColumns =
    [
        "entry_premium_px", "exit_premium_px", "strike_px", "r_equiv", "cash_r", "gross_inr", "net_inr", "units", "z_entry", "z_exit", "qty_y", "qty_x", "close_px", "open_px", "high_px", "low_px", "bid", "ask", "iv", "delta", "gamma", "vega", "theta", "value", "threshold",
    ];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static IEnumerable<Type> ViewTypes() => Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.OptionReports" && t.IsPublic && t.IsSealed && t.GetProperties().Length > 0 && !t.IsAbstract && t.Name != "OptionReportErrors" && !t.Name.EndsWith("Query", StringComparison.Ordinal));

    private static IEnumerable<string> Tokens(string name) => Regex.Matches(name, "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.None, ScanRegex.Timeout).Select(m => m.Value.ToLowerInvariant());

    [Fact]
    public void The_handlers_take_read_ports_only()
    {
        var handlers = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.OptionReports" && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        handlers.Select(t => t.Name).Order(StringComparer.Ordinal).ShouldBe(["GetExpressionRunHandler", "GetOptionArchiveSummaryHandler", "GetOptionsReportHandler", "GetP14VerdictListHandler", "ListExpressionRunsHandler", "ListPairFormationsHandler"]);
        foreach (var handler in handlers)
        {
            handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ShouldAllBe(n => AllowedParameters.Contains(n), handler.Name);
        }
    }

    [Fact]
    public void The_reader_port_has_exactly_five_reads_each_with_a_range_an_id_a_profile_or_a_limit()
    {
        var methods = typeof(IOptionReportReader).GetMethods();

        methods.Select(m => m.Name).Order(StringComparer.Ordinal).ShouldBe(["ArchiveUnderlyingsAsync", "ExpressionRunAsync", "ExpressionRunsAsync", "PairFormationsAsync"]);
        typeof(IOptionReportReader).GetProperties().ShouldBeEmpty();
        methods.ShouldAllBe(m => m.GetParameters().Any(p => p.Name == "limit" || p.Name == "runId"));
    }

    [Fact]
    public void The_reader_issues_selects_only_every_query_is_bounded_and_no_price_iv_greek_bar_quote_or_return_column_is_selected()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "OptionReports", "OptionReportReader.cs"));
        var queries = text.Split("CreateCommand(").Skip(1).Select(c => c[..Math.Min(c.Length, c.IndexOf("await using", StringComparison.Ordinal) is var i and > 0 ? i : c.Length)]).ToList();

        foreach (var banned in new[] { "INSERT", "UPDATE ", "DELETE", "TRUNCATE", "DROP ", "ALTER ", "COPY ", "CREATE ", "ExecuteNonQuery", "BeginBinaryImport", "execution.", "trading." })
        {
            text.ShouldNotContain(banned, Case.Sensitive);
        }

        queries.Count.ShouldBe(5);
        // Each query filters by a date range, a run id or a profile in a WHERE; the one exception is the newest-N list of runs, ordered by its time and cut by a limit.
        queries.ShouldAllBe(q => Regex.IsMatch(q, @"WHERE[^;]*(session_date >= @from|window_to >= @from|run_id = @id|run_id = ANY\(@ids\)|profile_hash = @profile)", RegexOptions.Singleline, ScanRegex.Timeout) || q.Contains("ORDER BY created_at DESC, run_id DESC LIMIT @limit", StringComparison.Ordinal));
        queries.ShouldAllBe(q => q.Contains("LIMIT @limit", StringComparison.Ordinal) || q.Contains("run_id = @id", StringComparison.Ordinal) || q.Contains("run_id = ANY(@ids)", StringComparison.Ordinal));
        foreach (var column in BannedColumns.Where(c => c is not "value" and not "threshold"))
        {
            Regex.IsMatch(string.Join(' ', queries.Select(q => q[..Math.Max(0, q.IndexOf("command.", StringComparison.Ordinal) is var k and > 0 ? k : q.Length)])), @"\b" + column + @"\b", RegexOptions.None, ScanRegex.Timeout).ShouldBeFalse(column);
        }

        Regex.Matches(text, @"(?:FROM|JOIN)\s+(\w+\.\w+)", RegexOptions.None, ScanRegex.Timeout).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(["options.quality_results", "research.expression_runs", "research.expression_trades", "research.pair_formations"]);
    }

    [Fact]
    public void The_response_types_carry_only_the_pinned_field_names_and_no_price_iv_greek_bar_quote_or_return_word()
    {
        var types = ViewTypes().ToList();
        types.ShouldNotBeEmpty();
        var names = types.SelectMany(t => t.GetProperties().Select(p => p.Name)).Distinct().Order(StringComparer.Ordinal).ToList();

        names.Except(AllowedNames).ShouldBeEmpty();
        names.SelectMany(Tokens).Intersect(BannedTokens).ShouldBeEmpty();
        typeof(XDrishti.Domain.Options.Quality.SessionOutcome).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(["Outcome", "Reason", "Sessions"]);
    }

    [Fact]
    public void The_routes_are_four_gets_that_name_no_table_store_or_reader()
    {
        var text = File.ReadAllText(Src("XDrishti.Api", "Endpoints", "OptionReportEndpoints.cs"));

        Regex.Count(text, @"\.MapGet\(", RegexOptions.None, ScanRegex.Timeout).ShouldBe(4);
        foreach (var banned in new[] { "MapPost", "MapPut", "MapDelete", "MapPatch", "IOptionReportReader", "IBackfillFetchLog", "IOptionQualityStore", "IExtensionEvaluationReader", "rolling_bars", "fetch_log", "quality_results" })
        {
            text.Replace("/options", string.Empty, StringComparison.Ordinal).Replace("/research", string.Empty, StringComparison.Ordinal).ShouldNotContain(banned, Case.Sensitive);
        }

        Regex.IsMatch(text, @"(FROM|JOIN)\s+\w+\.\w+", RegexOptions.None, ScanRegex.Timeout).ShouldBeFalse();
        text.ShouldContain("\"/summary\"");
        text.ShouldContain("\"/expression-runs\"");
        text.ShouldContain("\"/expression-runs/{id}\"");
        text.ShouldContain("\"/pair-formations\"");
    }

    [Fact]
    public void The_reports_name_no_execution_selection_planning_live_broker_or_network_type()
    {
        string[] banned =
        [
            "XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.Abstractions.Execution", "XDrishti.Application.Selection", "XDrishti.Application.Abstractions.Selection", "XDrishti.Application.Planning", "XDrishti.Application.Abstractions.Planning",
            "XDrishti.Application.Live", "XDrishti.Application.Abstractions.Live", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Infrastructure.Brokers", "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "System.Net.Http", "System.Net.WebSockets", "XDrishti.Application.Paper",
        ];
        foreach (var (assembly, ns) in new[] { (Application, "XDrishti.Application.OptionReports"), (Application, "XDrishti.Application.Abstractions.OptionReports"), (Infrastructure, "XDrishti.Infrastructure.OptionReports"), (Domain, "XDrishti.Domain.Options.Reports") })
        {
            var types = Types.InAssembly(assembly).That().ResideInNamespace(ns);
            types.GetTypes().ShouldNotBeEmpty(ns);
            var result = types.ShouldNot().HaveDependencyOnAny(banned).GetResult();
            result.IsSuccessful.ShouldBeTrue(ns + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void No_decision_live_or_execution_code_names_a_report_type()
    {
        foreach (var area in new[] { "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Live", "XDrishti.Application.Paper", "XDrishti.Application.Learning", "XDrishti.Application.LearningPhase" })
        {
            Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(Namespaces).GetResult().IsSuccessful.ShouldBeTrue(area);
        }

        foreach (var name in new[] { "XDrishti.Execution", "XDrishti.Simulation", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Strategies", "XDrishti.Indicators", "XDrishti.Learning.MlNet" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(Namespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }
}
