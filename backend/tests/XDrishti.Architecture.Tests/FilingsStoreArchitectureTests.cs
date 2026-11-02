using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-02 criteria 1, 12 and 13 (ADR 0180): the point-in-time filing store has no decision use yet. The port of the point-in-time read is named by one use case and the store that implements it; the decision layers, the pure libraries and the execution service
/// reference no filings type; nothing in the slice writes the fact tables, updates or deletes a row, reaches a network or logs a path; visibility is decided by the broadcast time and nothing else. P15-04, P15-05 and P15-06 amend the allow-lists below deliberately,
/// naming the new caller.
/// </summary>
public sealed partial class FilingsStoreArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] FilingsNamespaces =
    [
        "XDrishti.Domain.Filings", "XDrishti.Application.Abstractions.Filings", "XDrishti.Application.Filings", "XDrishti.Infrastructure.Filings", "XDrishti.Infrastructure.FilingArchive",
    ];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static string[] Files(string project, params string[] folder) => Directory.GetFiles(Src([project, .. folder]), "*.cs", SearchOption.AllDirectories);

    [Theory]
    [InlineData("XDrishti.Application.Portfolio")]
    [InlineData("XDrishti.Application.Execution")]
    [InlineData("XDrishti.Application.LiveTrading")]
    [InlineData("XDrishti.Application.Live")]
    [InlineData("XDrishti.Application.Lab")]
    [InlineData("XDrishti.Application.Paper")]
    [InlineData("XDrishti.Application.Depth")]
    [InlineData("XDrishti.Application.Ticks")]
    [InlineData("XDrishti.Application.OptionChain")]
    [InlineData("XDrishti.Application.OptionBackfill")]
    [InlineData("XDrishti.Application.OptionQuality")]
    [InlineData("XDrishti.Application.Market")]
    public void The_decision_live_and_recorder_code_references_no_filings_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(FilingsNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_domain_outside_the_filings_folder_the_pure_libraries_and_the_execution_service_reference_no_filings_type()
    {
        // P15-06 amends this deliberately: three Domain types and, in the simulator, three types name the filings types the event-risk filter needs; EventFilterArchitectureTests pins the exact sets.
        var domain = Types.InAssembly(Domain).That().DoNotResideInNamespaceStartingWith("XDrishti.Domain.Filings").And().DoNotHaveNameMatching(EventFilterUsers.Only(EventFilterUsers.DomainTypes))
            .ShouldNot().HaveDependencyOnAny(FilingsNamespaces).GetResult();

        domain.IsSuccessful.ShouldBeTrue("Domain violations: " + string.Join(", ", domain.FailingTypeNames ?? []));
        foreach (var name in new[] { "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Execution", "XDrishti.Learning.MlNet", "XDrishti.Mcp", "XDrishti.Feed", "XDrishti.Worker" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(FilingsNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void Only_the_asOf_list_handler_and_its_implementation_name_the_point_in_time_port()
    {
        var port = typeof(IPointInTimeFilings).FullName!;

        var application = Types.InAssembly(Application).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order().ToList();
        var infrastructure = Types.InAssembly(Infrastructure).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order().ToList();
        var api = Types.InAssembly(Assembly.Load(new AssemblyName("XDrishti.Api"))).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Distinct().ToList();

        application.ShouldBe(["ListFilingsHandler"]);
        infrastructure.ShouldBe(["DependencyInjection", "PointInTimeFilings"]);
        api.ShouldBeEmpty();
    }

    [Fact]
    public void The_store_the_reader_and_the_files_are_named_only_by_the_ingest_the_read_handlers_and_their_implementations()
    {
        var allowed = new Dictionary<string, string[]>
        {
            [typeof(IFilingStore).FullName!] = ["IngestFilingsHandler"],
            [typeof(IFilingReader).FullName!] = ["GetFilingCoverageHandler", "GetFilingFactsHandler", "GetFilingHandler", "ListFilingsHandler"],
            [typeof(IFilingFiles).FullName!] = ["IngestFilingsHandler", "ParseResultsHandler"],   // P15-03: the parse step opens the stored copy read-only
            [typeof(IResultFactsStore).FullName!] = ["ParseResultsHandler"],                    // P15-03: the only writer of result_facts and result_parses
            [typeof(IResultsXbrlReader).FullName!] = ["ParseResultsHandler"],
            [typeof(IResultFactRows).FullName!] = ["GetFilingFactsHandler", "ResultFactsAsOf"],
            [typeof(IFilingInstruments).FullName!] = ["GetEventFilterHandler", "GetResultsCalendarHandler", "IngestFilingsHandler", "ListFilingsHandler", "LoadCalendarHandler", "LoadShareholdingHandler"],
        };
        foreach (var (port, names) in allowed)
        {
            var found = Types.InAssembly(Application).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order().ToList();

            found.ShouldBe([.. names.Order()], port);
        }
    }

    [Fact]
    public void Only_the_filings_code_its_wiring_its_json_context_and_its_route_reference_a_filings_type_in_any_assembly()
    {
        foreach (var name in new[] { "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain", "XDrishti.Api", "XDrishti.Hosting" })
        {
            var result = Types.InAssembly(Assembly.Load(new AssemblyName(name))).That()
                .DoNotResideInNamespaceStartingWith("XDrishti.Domain.Filings").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Abstractions.Filings").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Filings")
                .And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.Filings").And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.FilingArchive")
                .And().DoNotHaveNameMatching(EventFilterUsers.WithRoots("DependencyInjection|ApiJsonContext.*|FilingEndpoints|CalendarEndpoints"))
                .ShouldNot().HaveDependencyOnAny(FilingsNamespaces).GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_ingest_and_the_archive_reach_no_network_broker_or_order_type()
    {
        string[] banned = ["System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "XDrishti.Infrastructure.Brokers", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Execution", "XDrishti.Application.Planning", "XDrishti.Application.LiveTrading"];

        var application = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Filings").ShouldNot().HaveDependencyOnAny([.. banned, "Npgsql", "XDrishti.Infrastructure"]).GetResult();
        var archive = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.FilingArchive").ShouldNot().HaveDependencyOnAny(banned).GetResult();

        application.IsSuccessful.ShouldBeTrue(string.Join(", ", application.FailingTypeNames ?? []));
        archive.IsSuccessful.ShouldBeTrue(string.Join(", ", archive.FailingTypeNames ?? []));
        foreach (var file in Files("XDrishti.Application", "Filings").Concat(Files("XDrishti.Infrastructure", "FilingArchive")))
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "HttpClient", "IHttpClientFactory", "WebClient", "BrokerEndpoints", "Dhan" })
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_archive_code_never_updates_deletes_or_truncates_a_row_and_writes_only_the_envelope_table_and_the_two_fact_tables_of_P15_04()
    {
        // P15-04 amends this test deliberately: the calendar store writes filings.calendar and the shareholding store writes filings.shareholding, each in its own file and nowhere else.
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CalendarStore.cs"] = ["filings.calendar"],
            ["ShareholdingStore.cs"] = ["filings.shareholding"],
            ["EventFilterDecisions.cs"] = ["filings.event_filter_decisions"],   // P15-06: the switch history, its own file and nowhere else
            ["ResultFactsStore.cs"] = ["filings.result_parses", "filings.result_facts"],   // P15-03: the parse rows and the facts, this file and nowhere else
        };
        foreach (var file in Files("XDrishti.Infrastructure", "FilingArchive"))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            Destructive().IsMatch(text).ShouldBeFalse(name + " has an UPDATE, DELETE or TRUNCATE statement");
            foreach (Match insert in InsertInto().Matches(text))
            {
                insert.Groups[1].Value.ShouldBeOneOf(allowed.TryGetValue(name, out var facts) ? ["filings.filings", .. facts] : ["filings.filings"], name);
            }
        }
    }

    [Fact]
    public void Nothing_in_the_shipped_source_writes_the_fact_tables_except_the_two_stores_of_P15_04()
    {
        var files = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)).ToList();
        files.ShouldNotBeEmpty();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            foreach (Match write in FactTableWrite().Matches(text))
            {
                var table = write.Groups[2].Value;
                var allowed = (name == "CalendarStore.cs" && table == "calendar") || (name == "ShareholdingStore.cs" && table == "shareholding") || (name == "ResultFactsStore.cs" && table is "result_facts" or "result_parses");

                allowed.ShouldBeTrue(file + " writes filings." + table + "; only ResultFactsStore (P15-03) writes result_facts and result_parses, P15-08 owns the extractions and only the two P15-04 stores write calendar and shareholding");
            }
        }
    }

    [Fact]
    public void The_ingest_and_the_archive_delete_no_folder_and_only_the_files_class_deletes_a_file()
    {
        foreach (var file in Files("XDrishti.Application", "Filings").Concat(Files("XDrishti.Infrastructure", "FilingArchive")))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("Directory.Delete", Case.Sensitive, Path.GetFileName(file));
            if (Path.GetFileName(file) != "FilingFiles.cs")
            {
                text.ShouldNotContain(".Delete(", Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_one_delete_of_the_files_class_is_the_temporary_copy_it_wrote_itself()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", "FilingFiles.cs"));

        Regex.Count(text, @"File\.Delete\(").ShouldBe(1);
        text.ShouldContain("File.Delete(temp)");
        text.ShouldContain("overwrite: false");
    }

    [Fact]
    public void Visibility_is_decided_by_the_broadcast_time_and_never_by_the_ingest_time_a_file_name_or_the_period()
    {
        var rule = File.ReadAllText(Src("XDrishti.Domain", "Filings", "PointInTimeRule.cs"));
        var read = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", "PointInTimeFilings.cs"));

        rule.ShouldNotContain("IngestedAt");
        rule.ShouldNotContain("FileName");
        read.ShouldNotContain("ingested_at");
        read.ShouldNotContain("period_end");
        read.ShouldNotContain("file_name");
        read.ShouldContain("broadcast_ts <= @cutoff");
        var visible = Regex.Match(rule, @"public static bool IsVisible.*?\n    }", RegexOptions.Singleline).Value;
        visible.ShouldNotContain("PeriodEnd");
        visible.ShouldContain("ts + lag <= t");
    }

    [Fact]
    public void The_domain_filings_types_do_no_io_and_read_no_clock()
    {
        foreach (var file in Files("XDrishti.Domain", "Filings"))
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "System.IO.File", "File.Read", "File.Write", "Directory.", "FileStream", "StreamReader", "Npgsql", "System.Net", "Microsoft.Extensions", "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow" })
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_ingest_log_carries_counts_ids_and_reason_codes_only_and_the_archive_logs_nothing()
    {
        var log = File.ReadAllText(Src("XDrishti.Application", "Filings", "FilingsIngestLog.cs"));
        var messages = Regex.Matches(log, "Message = \"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        messages.Count.ShouldBeGreaterThan(5);
        foreach (var banned in new[] { "path", "file_name", "filename", "source_ref", "sourceref", "content", "bytes_of", "headline" })
        {
            string.Join(" ", messages).ToUpperInvariant().ShouldNotContain(banned.ToUpperInvariant());
        }

        foreach (var file in Files("XDrishti.Infrastructure", "FilingArchive"))
        {
            File.ReadAllText(file).ShouldNotContain("ILogger", Case.Sensitive, Path.GetFileName(file));
        }
    }

    [Fact]
    public void The_migration_grants_table_by_table_insert_only_and_names_no_other_role()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "Persistence", "Migrations", "20261102060000_AddFilingsStore.cs"));

        text.ShouldContain("GrantTableToAppRole(Schemas.Filings, table)");
        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Filings, table)");
        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldNotContain("xd_exec");
        text.ShouldNotContain("xd_mcp");
        text.ShouldNotContain("GRANT UPDATE");
        text.ShouldNotContain("GRANT ALL");
        text.ShouldNotContain("filings.extractions"); // P15-08 owns it
        text.ShouldContain("SET search_path = pg_catalog, pg_temp");
    }

    [Fact]
    public void The_store_namespaces_hold_types_so_the_rules_are_not_vacuous()
    {
        Application.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Application.Filings" && t.Name == "IngestFilingsHandler");
        Infrastructure.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Infrastructure.FilingArchive" && t.Name == "PointInTimeFilings");
        Domain.GetTypes().ShouldContain(t => t.Namespace == "XDrishti.Domain.Filings" && t.Name == "PointInTimeRule");
    }

    [GeneratedRegex(@"\b(UPDATE\s+\w|DELETE\s+FROM|TRUNCATE\s+\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Destructive();

    [GeneratedRegex(@"INSERT\s+INTO\s+([\w.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InsertInto();

    [GeneratedRegex(@"(INSERT\s+INTO|COPY)\s+filings\.(result_facts|result_parses|shareholding|announcements|calendar)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FactTableWrite();
}
