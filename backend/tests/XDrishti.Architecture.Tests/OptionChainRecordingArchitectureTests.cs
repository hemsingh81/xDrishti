using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-02 criteria 10 and 11 (ADR 0176): the option-chain recorder is never on the trading path and nothing reads what it records. The decision layers, the live feed's code, the other recorders and the execution service reference no chain type; the store
/// has no method that reads a snapshot; no API route reads chain data; the client uses the two allow-listed constants only and never logs; the request spacing cannot be set under three seconds; the log events carry no secret.
/// </summary>
public sealed class OptionChainRecordingArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
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
    public void The_decision_live_and_recorder_code_references_no_chain_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(ChainNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>The simple names of the only types outside the chain namespaces that may name a chain type: the DI roots, the one refusal of "Run now", the switch's routes and JSON, the broker client with its throttle, and the host's options and loop.</summary>
    private const string MayReference = "^(DependencyInjection|RunServiceNow.*|OptionChain.*|ApiJsonContext.*|FeedHost|OptionsOptions|ChainOptions|ChainExpiryRuleOptions|DhanOptionChainClient|ChainThrottle)$";

    [Fact]
    public void Only_the_chain_code_its_wiring_and_its_routes_reference_a_chain_type_in_any_assembly_of_the_solution()
    {
        // An allow-list: every assembly of the solution (Application, Ai, Assistant, Autonomy, Market, Options, Mcp, MlNet, Worker, Api, Feed ...) is scanned and only the named types may reference a chain type.
        var names = new[] { "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain", "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Learning.MlNet", "XDrishti.Execution", "XDrishti.Hosting", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader" };
        foreach (var name in names)
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            var result = Types.InAssembly(assembly).That()
                .DoNotResideInNamespaceStartingWith("XDrishti.Application.OptionChain").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Abstractions.OptionChain").And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionChain")
                .And().DoNotHaveNameMatching(MayReference)
                .ShouldNot().HaveDependencyOnAny("XDrishti.Application.OptionChain", "XDrishti.Application.Abstractions.OptionChain", "XDrishti.Infrastructure.OptionChain").GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_domain_the_pure_libraries_and_the_execution_service_reference_no_chain_type()
    {
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(ChainNamespaces).GetResult().IsSuccessful.ShouldBeTrue();
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Execution" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(ChainNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void Only_the_chain_code_the_host_wiring_and_the_broker_client_reference_a_chain_type_in_infrastructure()
    {
        var offenders = Types.InAssembly(Infrastructure).That().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OptionChain").And().DoNotResideInNamespace("XDrishti.Infrastructure")
            .And().DoNotResideInNamespace("XDrishti.Infrastructure.Brokers")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.OptionChain", "XDrishti.Application.Abstractions.OptionChain").GetResult();

        offenders.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", offenders.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_chain_code_is_no_active_set_provider_and_touches_no_order_plan_live_session_or_other_recorder_store()
    {
        var provider = Application.GetType("XDrishti.Application.Live.IActiveSetProvider").ShouldNotBeNull();
        var chainTypes = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.OptionChain").ToList();
        chainTypes.ShouldNotBeEmpty();
        chainTypes.ShouldNotContain(t => provider.IsAssignableFrom(t));

        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.OptionChain").ShouldNot().HaveDependencyOnAny(
            "XDrishti.Application.Abstractions.Depth.IDepthStore",
            "XDrishti.Application.Abstractions.Depth.IDepthSwitchStore",
            "XDrishti.Application.Abstractions.Ticks",
            "XDrishti.Application.Ticks",
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
    public void The_chain_store_has_no_method_that_reads_a_snapshot_and_names_no_other_table()
    {
        typeof(IOptionChainStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order()
            .ShouldBe(["AppendAsync", "FindUnfinishedDaysAsync", "FreeBytesAsync", "RecordSessionAsync", "StoredBytesAsync"]);
        typeof(IOptionChainStore).GetMethods().Select(m => m.ReturnType.ToString()).ShouldAllBe(t => !t.Contains("ChainSnapshotRow", StringComparison.Ordinal));
        typeof(IOptionChainStore).GetProperties().ShouldBeEmpty();

        var store = Infrastructure.GetType("XDrishti.Infrastructure.OptionChain.OptionChainStore").ShouldNotBeNull();
        var declared = store.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && !m.Name.StartsWith('<')).Select(m => m.Name);
        declared.ShouldBeSubsetOf([.. typeof(IOptionChainStore).GetMethods().Select(m => m.Name), "WriteAsync"]);

        var text = Source("XDrishti.Infrastructure", "OptionChain", "OptionChainStore.cs");
        text.ShouldNotContain("FROM options.chain_snapshots", Case.Insensitive);
        foreach (var write in new[] { "market.", "execution.", "trading.", "UPDATE options", "DELETE FROM options", "TRUNCATE " })
        {
            text.ShouldNotContain(write, Case.Sensitive);
        }
    }

    [Fact]
    public void Only_the_chain_store_names_the_snapshot_table_in_the_whole_source()
    {
        var named = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "\"[^\"\\n]*chain_snapshots", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(Path.GetFileName).Order().ToList();

        named.ShouldBe(["OptionChainStore.cs"]);
    }

    [Fact]
    public void No_api_route_reads_chain_data_the_two_routes_are_the_switch()
    {
        var api = Path.Combine(Root, "src", "XDrishti.Api");
        var routes = File.ReadAllText(Path.Combine(api, "Endpoints", "OptionChainEndpoints.cs"));
        routes.ShouldContain("MapGet(\"/chain-recorder\"");
        routes.ShouldContain("MapPut(\"/chain-recorder\"");
        routes.Split("group.Map").Length.ShouldBe(3, "exactly two routes: the switch's read and its change");
        Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)).ShouldAllBe(f => !File.ReadAllText(f).Contains("chain_snapshots", StringComparison.Ordinal));
        Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)).ShouldAllBe(f => !File.ReadAllText(f).Contains("MapGet(\"/chain\"", StringComparison.Ordinal), "GET /api/options/chain belongs to P14-16");
    }

    [Fact]
    public void The_client_uses_the_two_allow_listed_urls_only_logs_nothing_and_sets_no_header_but_the_two()
    {
        var text = Source("XDrishti.Infrastructure", "Brokers", "DhanOptionChainClient.cs");
        text.ShouldContain("BrokerEndpoints.DhanOptionChain");
        text.ShouldContain("BrokerEndpoints.DhanOptionChainExpiryList");
        text.ShouldNotContain("new Uri(");
        text.ShouldNotContain("ILogger");
        text.ShouldNotContain("BuildFeedUri");
        text.ShouldNotContain("QueryString");
        System.Text.RegularExpressions.Regex.Matches(text, "TryAddWithoutValidation\\(\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)).Select(m => m.Groups[1].Value).Order().ShouldBe(["access-token", "client-id"]);
        var endpoints = Source("XDrishti.Infrastructure", "Brokers", "BrokerEndpoints.cs");
        System.Text.RegularExpressions.Regex.Count(endpoints, "new\\(\"https://api\\.dhan\\.co/v2/optionchain[^\"]*\"\\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)).ShouldBe(2);
        endpoints.ShouldContain("rollingoption", Case.Sensitive); // the one row of P14-03 (ADR 0177); the option-chain client uses only its own two constants
        System.Text.RegularExpressions.Regex.Count(endpoints, "new\\(\"https://api\\.dhan\\.co/v2/charts/rollingoption\"\\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)).ShouldBe(1);
    }

    [Fact]
    public void The_spacing_setting_cannot_be_set_under_three_seconds()
    {
        var chain = Source("XDrishti.Feed", "OptionsOptions.cs");
        chain.ShouldContain("[Range(3000, 60000)]");
        OptionChainSettings.MinSpacing.ShouldBe(TimeSpan.FromSeconds(3));
        OptionChainSettings.Off.Spacing.ShouldBe(TimeSpan.FromSeconds(3));
        OptionChainSettings.Off.Enabled.ShouldBeFalse();
        OptionChainSettings.Off.MaxStoredBytes.ShouldBe(4L * OptionChainSettings.Gb);
        Source("XDrishti.Infrastructure", "Brokers", "SpacingThrottle.cs").ShouldContain("spacing < floor ? floor : spacing");
    }

    [Fact]
    public void The_chain_log_ids_are_in_their_own_range_and_the_messages_name_no_secret_url_or_symbol()
    {
        var methods = Application.GetType("XDrishti.Application.OptionChain.OptionChainLog")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        var messages = methods.SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).ToList();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(a => a.EventId >= 6911 && a.EventId <= 6940);
        foreach (var text in messages.Select(m => m.Message ?? string.Empty))
        {
            text.ShouldStartWith("options.chain.");
            foreach (var placeholder in new[] { "{Token", "{Client", "{Url", "{Uri", "{Symbol", "{Price", "{Security", "{Query", "{Body", "{Header", "{Scrip" })
            {
                text.ShouldNotContain(placeholder, Case.Insensitive);
            }

            text.ShouldNotContain("https://", Case.Insensitive);
        }

        foreach (var word in new[] { "request", "gap", "slot", "disk_warning", "disk_stop", "token_refused", "paused" })
        {
            messages.ShouldContain(a => (a.Message ?? string.Empty).StartsWith("options.chain." + word, StringComparison.Ordinal), word);
        }
    }

    [Fact]
    public void The_log_event_ids_of_the_whole_application_are_unique()
    {
        var ids = Application.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).Select(a => a.EventId).Where(id => id is >= 6911 and <= 6940).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
    }
}
