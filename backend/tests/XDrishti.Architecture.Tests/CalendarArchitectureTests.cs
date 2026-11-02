using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-04 criterion 11 (ADR 0181): the results calendar and the shareholding reads have no decision use yet. <c>IResultsCalendar</c> and <c>IShareholdingAsOf</c> are named only by their own implementations, the read handler of the route and the tests; the checklists, the
/// simulation, the strategies, planning, learning, selection and the execution service reference neither (nor any filings type). The loaders touch only local files and their tables. P15-05 and P15-06 amend the allow-lists below deliberately, naming the new caller.
/// </summary>
public sealed partial class CalendarArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Named(Assembly assembly, string port) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order()];

    [Fact]
    public void Only_the_calendar_implementation_and_the_route_handler_name_the_results_calendar_port()
    {
        var port = typeof(IResultsCalendar).FullName!;

        Named(Application, port).ShouldBe(["DependencyInjection", "GetResultsCalendarHandler", "ResultsCalendar"]);
        Named(Infrastructure, port).ShouldBeEmpty();
        Named(Assembly.Load(new AssemblyName("XDrishti.Api")), port).ShouldBeEmpty();
        Named(Domain, port).ShouldBeEmpty();
    }

    [Fact]
    public void Only_its_own_implementation_names_the_shareholding_port()
    {
        var port = typeof(IShareholdingAsOf).FullName!;

        Named(Application, port).ShouldBe(["DependencyInjection", "ShareholdingAsOf"]);
        Named(Infrastructure, port).ShouldBeEmpty();
        Named(Assembly.Load(new AssemblyName("XDrishti.Api")), port).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("XDrishti.Application.Planning")]
    [InlineData("XDrishti.Application.Learning")]
    [InlineData("XDrishti.Application.Selection")]
    [InlineData("XDrishti.Application.Execution")]
    [InlineData("XDrishti.Application.LiveTrading")]
    [InlineData("XDrishti.Application.Portfolio")]
    [InlineData("XDrishti.Application.Research")]
    [InlineData("XDrishti.Application.Lab")]
    [InlineData("XDrishti.Application.Paper")]
    [InlineData("XDrishti.Application.Live")]
    public void The_decision_areas_reference_neither_calendar_port_nor_any_filings_type(string area)
    {
        var banned = new[] { typeof(IResultsCalendar).FullName!, typeof(IShareholdingAsOf).FullName!, "XDrishti.Domain.Filings", "XDrishti.Application.Abstractions.Filings", "XDrishti.Application.Filings" };

        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_checklist_facts_the_execution_check_facts_and_the_pure_libraries_never_mention_the_ports_or_the_rule()
    {
        var domainChecklist = Types.InAssembly(Domain).That().ResideInNamespaceStartingWith("XDrishti.Domain.Checklist").ShouldNot().HaveDependencyOnAny("XDrishti.Domain.Filings").GetResult();
        domainChecklist.IsSuccessful.ShouldBeTrue(string.Join(", ", domainChecklist.FailingTypeNames ?? []));
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Execution", "XDrishti.Indicators" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny("XDrishti.Domain.Filings", typeof(IResultsCalendar).FullName!, typeof(IShareholdingAsOf).FullName!).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void Nothing_outside_the_two_ports_sets_days_to_results_from_the_calendar()
    {
        foreach (var file in Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("DaysToResults", StringComparison.Ordinal) && text.Contains("IResultsCalendar", StringComparison.Ordinal))
            {
                Path.GetFileName(file).ShouldBeOneOf("IResultsCalendar.cs", "ResultsCalendar.cs", "GetResultsCalendarHandler.cs");
            }
        }
    }

    [Fact]
    public void The_loaders_reach_no_network_broker_or_order_type_and_delete_nothing()
    {
        string[] banned = ["System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "XDrishti.Infrastructure.Brokers", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Execution", "XDrishti.Application.Planning", "XDrishti.Application.LiveTrading"];
        var loaders = Types.InAssembly(Application).That().HaveNameMatching("^(LoadCalendarHandler|LoadShareholdingHandler|RowFileReader|RowLoadCounts|ResultsCalendar|ShareholdingAsOf|GetResultsCalendarHandler)$").ShouldNot().HaveDependencyOnAny(banned).GetResult();

        loaders.IsSuccessful.ShouldBeTrue(string.Join(", ", loaders.FailingTypeNames ?? []));
        foreach (var name in new[] { "LoadCalendarHandler", "LoadShareholdingHandler", "RowFileReader", "ResultsCalendar", "ShareholdingAsOf", "GetResultsCalendarHandler" })
        {
            var text = File.ReadAllText(Src("XDrishti.Application", "Filings", name + ".cs"));
            foreach (var word in new[] { "HttpClient", "File.Delete", "Directory.", "File.Write", "File.Move", "BrokerEndpoints", "Dhan" })
            {
                text.ShouldNotContain(word, Case.Sensitive, name);
            }
        }
    }

    [Fact]
    public void The_loader_log_carries_counts_and_reason_codes_only()
    {
        var log = File.ReadAllText(Src("XDrishti.Application", "Filings", "RowLoadLog.cs"));
        var messages = string.Join(" ", MessageText().Matches(log).Select(m => m.Groups[1].Value)).ToUpperInvariant();

        messages.ShouldNotBeEmpty();
        foreach (var banned in new[] { "PATH", "FILE_NAME", "FILENAME", "SYMBOL", "CELL", "CONTENT", "LINE=" })
        {
            messages.ShouldNotContain(banned);
        }
    }

    [Fact]
    public void The_calendar_and_shareholding_stores_insert_only_and_the_readers_select_only()
    {
        foreach (var name in new[] { "CalendarStore", "ShareholdingStore", "CalendarRows", "ShareholdingRows" })
        {
            var text = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", name + ".cs"));

            text.ShouldNotContain("ILogger", Case.Sensitive, name);
            Regex.IsMatch(text, @"\b(UPDATE\s+\w|DELETE\s+FROM|TRUNCATE)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).ShouldBeFalse(name);
            if (name.EndsWith("Rows", StringComparison.Ordinal))
            {
                text.ShouldNotContain("INSERT", Case.Sensitive, name);
            }
        }
    }

    [Fact]
    public void Visibility_in_the_calendar_reads_is_the_intimation_time_and_never_the_meeting_date_the_ingest_time_or_the_file_name()
    {
        var rows = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", "CalendarRows.cs"));
        var rule = File.ReadAllText(Src("XDrishti.Domain", "Filings", "ResultsDays.cs"));

        rows.ShouldContain("f.broadcast_ts <= @cutoff");
        rows.ShouldNotContain("ingested_at");
        rows.ShouldNotContain("file_name");
        Regex.IsMatch(rows, @"WHERE[^;]*meeting_date", RegexOptions.Singleline).ShouldBeFalse("the meeting date must not narrow what is visible");
        rule.ShouldNotContain("IngestedAt");
        rule.ShouldNotContain("FileName");
        rule.ShouldContain("PointInTimeRule.IsVisible");
        rule.ShouldContain("PointInTimeRule.AsOf");
    }

    [GeneratedRegex("Message = \"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex MessageText();
}
