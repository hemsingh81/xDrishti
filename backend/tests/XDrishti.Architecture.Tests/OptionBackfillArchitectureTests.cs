using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-03 criteria 8 and 9 (ADR 0177): the expired-options backfill is never on the trading path and nothing reads what it records. The decision layers, the live feed's code, the other recorders and the execution service reference no backfill type; the bar store has
/// no method that reads a bar; no API route reads backfill data; the client uses the one allow-listed constant and never logs; the request rate cannot be set above two a second; the probe holds no store; the log events carry no secret.
/// </summary>
public sealed class OptionBackfillArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly string[] BackfillNamespaces = ["XDrishti.Application.OptionBackfill", "XDrishti.Application.Abstractions.OptionBackfill", "XDrishti.Infrastructure.OptionBackfill"];
    private static readonly string[] ChainNamespaces = ["XDrishti.Application.OptionChain", "XDrishti.Application.Abstractions.OptionChain", "XDrishti.Infrastructure.OptionChain"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

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
    public void The_decision_live_and_recorder_code_references_no_backfill_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(BackfillNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>The simple names of the only types outside the backfill namespaces that may name a backfill type: the DI roots, the one refusal of "Run now", the routes and JSON, the broker client with its throttle, and the host's options, loop and probe command.</summary>
    private const string MayReference = "^(DependencyInjection|RunServiceNow.*|OptionBackfill.*|ApiJsonContext.*|FeedHost|OptionsOptions|BackfillOptions|BackfillProbeCommand|DhanRollingOptionClient|BackfillThrottle)$";

    [Fact]
    public void Only_the_backfill_code_its_wiring_and_its_routes_reference_a_backfill_type_in_any_assembly_of_the_solution()
    {
        var names = new[] { "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain", "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Learning.MlNet", "XDrishti.Execution", "XDrishti.Hosting", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader" };
        foreach (var name in names)
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            var result = Types.InAssembly(assembly).That()
                .DoNotResideInNamespaceStartingWith("XDrishti.Application.OptionBackfill").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Abstractions.OptionBackfill").And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionBackfill")
                .And().DoNotHaveNameMatching(MayReference)
                .ShouldNot().HaveDependencyOnAny(BackfillNamespaces).GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_domain_the_pure_libraries_the_chain_code_and_the_execution_service_reference_no_backfill_type()
    {
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(BackfillNamespaces).GetResult().IsSuccessful.ShouldBeTrue();
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Execution" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(BackfillNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }

        Types.InAssembly(Infrastructure).That().ResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionChain").ShouldNot().HaveDependencyOnAny(BackfillNamespaces).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Only_the_backfill_code_the_host_wiring_and_the_broker_client_reference_a_backfill_type_in_infrastructure()
    {
        var offenders = Types.InAssembly(Infrastructure).That().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionBackfill").And().DoNotResideInNamespace("XDrishti.Infrastructure")
            .And().DoNotResideInNamespace("XDrishti.Infrastructure.Brokers")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.OptionBackfill", "XDrishti.Application.Abstractions.OptionBackfill").GetResult();

        offenders.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", offenders.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_backfill_code_is_no_active_set_provider_and_touches_no_order_plan_live_session_chain_or_other_recorder_store()
    {
        var provider = Application.GetType("XDrishti.Application.Live.IActiveSetProvider").ShouldNotBeNull();
        var types = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.OptionBackfill").ToList();
        types.ShouldNotBeEmpty();
        types.ShouldNotContain(t => provider.IsAssignableFrom(t));

        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.OptionBackfill").ShouldNot().HaveDependencyOnAny(
            "XDrishti.Application.Abstractions.Depth.IDepthStore",
            "XDrishti.Application.Abstractions.Depth.IDepthSwitchStore",
            "XDrishti.Application.Abstractions.Ticks",
            "XDrishti.Application.Ticks",
            "XDrishti.Application.Abstractions.OptionChain",
            "XDrishti.Application.OptionChain",
            "XDrishti.Application.Abstractions.Planning",
            "XDrishti.Application.Abstractions.Live.ILiveFlush",
            "XDrishti.Application.Abstractions.Live.ILiveFeedConnector",
            "XDrishti.Application.Abstractions.Live.ILiveQuotes",
            "XDrishti.Application.Live.IFeedSession",
            "XDrishti.Application.Live.FeedConnectionManager",
            "XDrishti.Application.Live.LiveFeedSupervisor",
            "XDrishti.Application.Live.IActiveSetProvider",
            "XDrishti.Application.Execution",
            "XDrishti.Application.LiveTrading",
            "XDrishti.Application.Planning",
            "XDrishti.Application.Learning",
            "XDrishti.Application.Portfolio",
            "XDrishti.Application.Paper",
            "XDrishti.Infrastructure",
            "Npgsql",
            "System.Net.WebSockets",
            "System.Net.Http").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_bar_store_has_no_method_that_reads_a_bar_and_names_no_other_table()
    {
        typeof(IRollingBarStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order().ShouldBe(["AppendAsync", "FreeBytesAsync", "StoredBytesAsync"]);
        typeof(IRollingBarStore).GetMethods().Select(m => m.ReturnType.ToString()).ShouldAllBe(t => !t.Contains("BackfillBar", StringComparison.Ordinal));
        typeof(IRollingBarStore).GetProperties().ShouldBeEmpty();
        typeof(IBackfillFetchLog).GetMethods().Select(m => m.Name).Order().ShouldBe(["CountSentSinceAsync", "LoadStateAsync"]); // the log is read for the resume state only

        var store = Infrastructure.GetType("XDrishti.Infrastructure.OptionBackfill.RollingBarStore").ShouldNotBeNull();
        var declared = store.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && !m.Name.StartsWith('<')).Select(m => m.Name);
        declared.ShouldBeSubsetOf([.. typeof(IRollingBarStore).GetMethods().Select(m => m.Name), "WriteAsync"]);

        var text = Source("XDrishti.Infrastructure", "OptionBackfill", "RollingBarStore.cs");
        text.ShouldNotContain("FROM options.rolling_bars", Case.Insensitive);
        foreach (var write in new[] { "market.", "execution.", "trading.", "UPDATE options", "DELETE FROM options", "TRUNCATE " })
        {
            text.ShouldNotContain(write, Case.Sensitive);
        }
    }

    [Fact]
    public void Only_the_bar_store_and_the_quality_reader_name_the_bar_table_and_only_the_log_the_store_and_the_quality_reader_name_the_fetch_log_in_the_whole_source()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)).ToList();
        string[] Naming(string table) => [.. files.Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "(FROM|INTO|UPDATE|JOIN)\\s+options\\." + table, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5))).Select(f => Path.GetFileName(f)).Order()];

        // P14-04 (ADR 0178) added exactly one reader: the quality checks' OptionArchiveReader; P14-08 (ADR 0197) added exactly one more, the re-mark's RollingBarReader (one series over one time range, and the fetch-log watermark). A further name here is a failure.
        Naming("rolling_bars").ShouldBe(["OptionArchiveReader.cs", "RollingBarReader.cs", "RollingBarStore.cs"]);
        Naming("fetch_log").ShouldBe(["BackfillFetchLog.cs", "OptionArchiveReader.cs", "RollingBarReader.cs", "RollingBarStore.cs"]);
    }

    [Fact]
    public void No_api_route_reads_backfill_data_the_three_routes_are_the_switch_and_the_acceptance()
    {
        var api = Path.Combine(Root, "src", "XDrishti.Api");
        var routes = File.ReadAllText(Path.Combine(api, "Endpoints", "OptionBackfillEndpoints.cs"));
        routes.ShouldContain("MapGet(\"/backfill\"");
        routes.ShouldContain("MapPut(\"/backfill\"");
        routes.ShouldContain("MapPut(\"/backfill/probe-acceptance\"");
        routes.Split("group.Map").Length.ShouldBe(4, "exactly three routes: the read, the switch and the acceptance");
        foreach (var name in new[] { "rolling_bars", "fetch_log" })
        {
            Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)).ShouldAllBe(f => !File.ReadAllText(f).Contains(name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_client_uses_the_one_allow_listed_url_only_logs_nothing_and_sets_no_header_but_the_two()
    {
        var text = Source("XDrishti.Infrastructure", "Brokers", "DhanRollingOptionClient.cs");
        text.ShouldContain("BrokerEndpoints.DhanRollingOption");
        text.ShouldNotContain("new Uri(");
        text.ShouldNotContain("ILogger");
        text.ShouldNotContain("QueryString");
        text.ShouldNotContain("BrokerEndpoints.DhanOptionChain");
        System.Text.RegularExpressions.Regex.Matches(text, "TryAddWithoutValidation\\(\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)).Select(m => m.Groups[1].Value).Order().ShouldBe(["access-token", "client-id"]);
        var endpoints = Source("XDrishti.Infrastructure", "Brokers", "BrokerEndpoints.cs");
        System.Text.RegularExpressions.Regex.Count(endpoints, "new\\(\"https://api\\.dhan\\.co/v2/charts/[^\"]*\"\\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)).ShouldBe(2, "intraday and rollingoption, nothing else under /v2/charts");
        endpoints.ShouldNotContain("charts/historical");
    }

    [Fact]
    public void The_rate_cannot_be_set_above_two_a_second_and_the_throttle_cannot_go_under_half_a_second()
    {
        Source("XDrishti.Feed", "OptionsOptions.cs").ShouldContain("[Range(0.2, 2.0)]");
        OptionBackfillSettings.MinSpacing.ShouldBe(TimeSpan.FromMilliseconds(500));
        OptionBackfillSettings.Off.Enabled.ShouldBeFalse();
        OptionBackfillSettings.Off.RequestsPerSecond.ShouldBe(1);
        OptionBackfillSettings.Off.MaxStoredBytes.ShouldBe(3L * OptionBackfillSettings.Gb);
        (OptionBackfillSettings.Off with { RequestsPerSecond = 100 }).Spacing.ShouldBe(TimeSpan.FromMilliseconds(500));
        Source("XDrishti.Infrastructure", "Brokers", "SpacingThrottle.cs").ShouldContain("spacing < floor ? floor : spacing");
    }

    [Fact]
    public void The_probe_holds_no_store_and_the_probe_command_calls_no_write()
    {
        var probe = Application.GetType("XDrishti.Application.OptionBackfill.BackfillProbe").ShouldNotBeNull();
        probe.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(IOptionBackfillClient), typeof(OptionBackfillSettings)]);
        Source("XDrishti.Application", "OptionBackfill", "BackfillProbe.cs").ShouldNotContain("Append");
        var command = Source("XDrishti.Feed", "BackfillProbeCommand.cs");
        command.ShouldNotContain("IRollingBarStore");
        command.ShouldNotContain("AppendAsync");
        command.ShouldNotContain("IBackfillFetchLog");
    }

    [Fact]
    public void The_backfill_log_ids_are_in_their_own_range_and_the_messages_name_no_secret_url_or_symbol()
    {
        var methods = Application.GetType("XDrishti.Application.OptionBackfill.OptionBackfillLog")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        var messages = methods.SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).ToList();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(a => a.EventId >= 6941 && a.EventId <= 6970);
        foreach (var text in messages.Select(m => m.Message ?? string.Empty))
        {
            text.ShouldStartWith("options.backfill.");
            foreach (var placeholder in new[] { "{Token", "{Client", "{Url", "{Uri", "{Symbol", "{Price", "{Security", "{Query", "{Body", "{Header", "{Scrip" })
            {
                text.ShouldNotContain(placeholder, Case.Insensitive);
            }

            text.ShouldNotContain("https://", Case.Insensitive);
        }

        foreach (var word in new[] { "window", "gap", "given_up", "disk_stop", "token_refused", "done" })
        {
            messages.ShouldContain(a => (a.Message ?? string.Empty).StartsWith("options.backfill." + word, StringComparison.Ordinal), word);
        }
    }

    [Fact]
    public void The_log_event_ids_of_the_whole_application_are_unique_and_the_blocks_do_not_overlap()
    {
        var ids = Application.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).Select(a => a.EventId).Where(id => id is >= 6901 and <= 6999).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
        Application.GetType("XDrishti.Application.OptionChain.OptionChainLog")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).ShouldAllBe(a => a.EventId <= 6940);
    }

    [Fact]
    public void The_deployment_flag_defaults_off_and_the_service_is_registered_paused()
    {
        Source("XDrishti.Feed", "OptionsOptions.cs").ShouldContain("public bool Enabled { get; set; }");
        File.ReadAllText(RepositoryFiles.RepositoryFile("deploy", "compose.yaml")).ShouldContain("Options__Backfill__Enabled: ${XD_OPTIONS_BACKFILL_ENABLED:-false}");
        XDrishti.Application.OptionBackfill.OptionBackfillService.Definition.Defaults.Enabled.ShouldBeFalse();
        Source("XDrishti.Application", "Platform", "Services", "RunServiceNow.cs").ShouldContain("OptionBackfillService.Id");
    }
}
