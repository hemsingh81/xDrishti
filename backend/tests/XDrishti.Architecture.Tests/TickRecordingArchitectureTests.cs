using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Ticks;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P7-16b criterion 11 (ADR 0173): the trade-tick recorder is never on the trading path and nothing reads what it records. The decision layers and the live feed's own code reference no tick type; the set is not an active-set provider; the tick code
/// writes no depth table and touches no live session; the store has no method that reads a tick; no API route reads ticks; the connector is the live feed's allowed one; the log events carry no secret.
/// </summary>
public sealed class TickRecordingArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly string[] TickNamespaces = ["XDrishti.Application.Ticks", "XDrishti.Application.Abstractions.Ticks", "XDrishti.Infrastructure.Ticks"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    [Theory]
    [InlineData("XDrishti.Application.Planning")]
    [InlineData("XDrishti.Application.Learning")]
    [InlineData("XDrishti.Application.Portfolio")]
    [InlineData("XDrishti.Application.Live")]
    [InlineData("XDrishti.Application.Selection")]
    [InlineData("XDrishti.Application.Research")]
    [InlineData("XDrishti.Application.Lab")]
    [InlineData("XDrishti.Application.Reporting")]
    [InlineData("XDrishti.Application.Paper")]
    [InlineData("XDrishti.Application.Depth")]
    public void The_decision_live_and_depth_code_references_no_tick_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(TickNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_domain_and_the_pure_libraries_reference_no_tick_type()
    {
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(TickNamespaces).GetResult().IsSuccessful.ShouldBeTrue();
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(TickNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void Only_the_tick_code_the_host_wiring_and_the_broker_connector_reference_a_tick_type_in_infrastructure()
    {
        var offenders = Types.InAssembly(Infrastructure).That().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.Ticks").And().DoNotResideInNamespace("XDrishti.Infrastructure")
            .And().DoNotResideInNamespace("XDrishti.Infrastructure.Brokers")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Ticks", "XDrishti.Application.Abstractions.Ticks").GetResult();

        offenders.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", offenders.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_tick_code_is_no_active_set_provider_writes_no_depth_table_and_touches_no_live_session_flush_or_plan_writer()
    {
        var provider = Application.GetType("XDrishti.Application.Live.IActiveSetProvider").ShouldNotBeNull();
        var tickTypes = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Ticks").ToList();
        tickTypes.ShouldNotBeEmpty();
        tickTypes.ShouldNotContain(t => provider.IsAssignableFrom(t));

        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Ticks").ShouldNot().HaveDependencyOnAny(
            "XDrishti.Application.Abstractions.Depth.IDepthStore",
            "XDrishti.Application.Abstractions.Depth.IDepthSwitchStore",
            "XDrishti.Application.Abstractions.Depth.IDepthFeedConnector",
            "XDrishti.Application.Depth.IDepthSession",
            "XDrishti.Application.Depth.DepthRecorder",
            "XDrishti.Application.Depth.DepthRecorderSupervisor",
            "XDrishti.Application.Abstractions.Planning.IPlanStore",
            "XDrishti.Application.Abstractions.Planning.IAutoApprovalStore",
            "XDrishti.Application.Abstractions.Live.ILiveFlush",
            "XDrishti.Application.Abstractions.Live.ILiveFeedConnector",
            "XDrishti.Application.Abstractions.Live.ILiveQuotes",
            "XDrishti.Application.Live.IFeedSession",
            "XDrishti.Application.Live.FeedConnectionManager",
            "XDrishti.Application.Live.LiveFeedSupervisor",
            "XDrishti.Application.Live.IActiveSetProvider",
            "XDrishti.Application.Live.ActiveSetLoader",
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
    public void The_tick_store_has_no_method_that_reads_a_tick_and_writes_no_depth_table()
    {
        typeof(ITickStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order()
            .ShouldBe(["AppendAsync", "FindUnfinishedDaysAsync", "FreeBytesAsync", "LoadDepthSetAsync", "LoadSetAsync", "RecordSessionAsync", "SaveSetAsync", "StoredBytesAsync"]);
        typeof(ITickStore).GetMethods().Select(m => m.ReturnType.ToString()).ShouldAllBe(t => !t.Contains("TradeTick", StringComparison.Ordinal));
        typeof(ITickStore).GetProperties().ShouldBeEmpty();

        var store = Infrastructure.GetType("XDrishti.Infrastructure.Ticks.TickStore").ShouldNotBeNull();
        var declared = store.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && !m.Name.StartsWith('<')).Select(m => m.Name);
        declared.ShouldBeSubsetOf([.. typeof(ITickStore).GetMethods().Select(m => m.Name), "LoadAsync"]);

        var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Ticks", "TickStore.cs"));
        foreach (var write in new[] { "INSERT INTO market.depth", "UPDATE market.depth", "DELETE FROM market.depth", "COPY market.depth", "depth_snapshots", "depth_sessions " })
        {
            text.ShouldNotContain(write, Case.Insensitive);
        }

        text.ShouldNotContain("FROM market.trade_ticks", Case.Insensitive);
    }

    [Fact]
    public void Only_the_tick_store_names_the_tick_table_in_the_whole_source()
    {
        var named = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "\"[^\"\\n]*trade_ticks", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(Path.GetFileName).Order().ToList();

        named.ShouldBe(["TickStore.cs"]);
    }

    [Fact]
    public void No_api_route_reads_ticks_the_two_routes_are_the_switch()
    {
        var api = Path.Combine(Root, "src", "XDrishti.Api");
        var routes = File.ReadAllText(Path.Combine(api, "Endpoints", "TickEndpoints.cs"));
        routes.ShouldContain("MapGet(\"/recorder\"");
        routes.ShouldContain("MapPut(\"/recorder\"");
        routes.Split("group.Map").Length.ShouldBe(3, "exactly two routes: the switch's read and its change");
        Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && File.ReadAllText(f).Contains("trade_ticks", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_tick_connector_is_the_live_feeds_allowed_connector_and_builds_no_url_of_its_own()
    {
        var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "TickFeedConnector.cs"));
        text.ShouldContain("new(clock, loggers, connectTo, new FeedPacketStats())", Case.Sensitive, "a DhanFeedConnector of its own, with packet counts of its own");
        text.ShouldNotContain("BuildFeedUri");
        text.ShouldNotContain("BuildDepthUri");
        text.ShouldNotContain("ClientWebSocket");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "DhanFeedConnector.cs")).ShouldNotContain("Tick");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "BrokerEndpoints.cs")).ShouldNotContain("tick", Case.Insensitive);
    }

    [Fact]
    public void The_tick_log_ids_are_in_their_own_range_and_the_messages_name_no_secret_or_symbol()
    {
        var methods = Application.GetType("XDrishti.Application.Ticks.TickLog")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        var messages = methods.SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).ToList();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(a => a.EventId >= 5341 && a.EventId <= 5370);
        foreach (var text in messages.Select(m => m.Message ?? string.Empty))
        {
            text.ShouldStartWith("ticks.");
            foreach (var placeholder in new[] { "{Token", "{Client", "{Url", "{Uri", "{Symbol", "{Price", "{Security", "{Query", "{Ltp", "{Volume" })
            {
                text.ShouldNotContain(placeholder, Case.Insensitive);
            }

            text.ShouldNotContain("wss://", Case.Insensitive);
        }

        foreach (var word in new[] { "connected", "disconnected", "subscribed", "flush", "dropped", "disk_warning", "disk_stop", "token_refused", "flapping" })
        {
            messages.ShouldContain(a => (a.Message ?? string.Empty).StartsWith("ticks." + word, StringComparison.Ordinal), word);
        }
    }
}
