using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P7-16 (ADR 0135): the depth recorder is never on the trading path and nothing reads what it records. The decision layers (Domain, Simulation, Strategies, Indicators, Planning, Learning, Portfolio and the live feed's own code) reference no depth type; the
/// recording set is not an active-set provider; the store has no method that reads a snapshot; no API route reads depth data and no hand-written frontend file mentions it; the depth code opens no order-capable scope. The no-order tripwire covers the new types too.
/// </summary>
public sealed class DepthRecordingArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly string[] DepthNamespaces = ["XDrishti.Application.Depth", "XDrishti.Application.Abstractions.Depth", "XDrishti.Infrastructure.Depth"];

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
    public void The_decision_and_live_code_of_the_application_layer_references_no_depth_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(DepthNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_domain_references_no_depth_type_and_no_pure_library_references_the_application_layer()
    {
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(DepthNamespaces).GetResult().IsSuccessful.ShouldBeTrue();
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader" })
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            assembly.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Application", name + " must not reach the application layer, so it cannot reach depth");
            Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(DepthNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void Only_the_depth_code_the_host_wiring_and_the_switch_endpoint_reference_a_depth_type()
    {
        var offenders = Types.InAssembly(Infrastructure).That().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.Depth").And().DoNotResideInNamespace("XDrishti.Infrastructure")
            .And().DoNotResideInNamespace("XDrishti.Infrastructure.Brokers")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Depth", "XDrishti.Application.Abstractions.Depth").GetResult();

        offenders.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", offenders.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_recording_set_is_not_an_active_set_provider_and_the_depth_code_touches_no_live_session_flush_or_plan_writer()
    {
        var provider = Application.GetType("XDrishti.Application.Live.IActiveSetProvider").ShouldNotBeNull();
        var depthTypes = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Depth").ToList();
        depthTypes.ShouldNotBeEmpty();
        depthTypes.ShouldNotContain(t => provider.IsAssignableFrom(t));

        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Depth").ShouldNot().HaveDependencyOnAny(
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
    public void The_depth_store_has_no_method_that_reads_a_snapshot()
    {
        typeof(IDepthStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order()
            .ShouldBe(["AppendAsync", "FindUnfinishedDaysAsync", "FreeBytesAsync", "LoadSetAsync", "RecordSessionAsync", "SaveSetAsync", "StoredBytesAsync"]);

        // nothing returns a snapshot: they only go in
        var returned = typeof(IDepthStore).GetMethods().Select(m => m.ReturnType.ToString());
        returned.ShouldAllBe(t => !t.Contains("DepthSnapshot", StringComparison.Ordinal));
        typeof(IDepthStore).GetProperties().ShouldBeEmpty();

        // the implementation adds nothing to the interface
        var store = Infrastructure.GetType("XDrishti.Infrastructure.Depth.DepthStore").ShouldNotBeNull();
        var declared = store.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && !m.Name.StartsWith('<')).Select(m => m.Name);
        declared.ShouldBeSubsetOf(typeof(IDepthStore).GetMethods().Select(m => m.Name));
    }

    [Fact]
    public void No_port_other_than_the_store_and_the_universe_names_a_depth_snapshot_and_the_universe_reads_bars_only()
    {
        var holders = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Abstractions.Depth" && t.IsInterface && t != typeof(IDepthStore))
            .SelectMany(t => t.GetMethods()).Select(m => m.ReturnType.ToString() + " " + string.Join(' ', m.GetParameters().Select(p => p.ParameterType.ToString())));
        holders.ShouldAllBe(s => !s.Contains("DepthSnapshot", StringComparison.Ordinal) || s.Contains("IAsyncEnumerable", StringComparison.Ordinal) == false);
        var universe = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Depth", "DepthUniverse.cs"));
        universe.ShouldNotContain("depth_snapshots");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Depth", "DepthSwitchStore.cs")).ShouldNotContain("depth_snapshots");
    }

    [Fact]
    public void Only_the_depth_store_names_the_snapshot_table_in_the_whole_source()
    {
        var root = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;
        var named = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "\"[^\"\\n]*depth_snapshots", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(Path.GetFileName).Order().ToList();

        named.ShouldBe(["DepthStore.cs"]);
    }

    [Fact]
    public void No_api_route_reads_depth_data_and_no_hand_written_frontend_file_mentions_depth()
    {
        var api = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!, "src", "XDrishti.Api");
        var mentioning = Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && File.ReadAllText(f).Contains("depth", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName).Order().ToList();
        mentioning.ShouldBe(["ApiJsonContext.cs", "ApiSetup.cs", "DepthEndpoints.cs", "DepthSwitchRequest.cs"]);

        var routes = File.ReadAllText(Path.Combine(api, "Endpoints", "DepthEndpoints.cs"));
        routes.ShouldContain("MapGet(\"/recorder\"");
        routes.ShouldContain("MapPut(\"/recorder\"");
        routes.Split("group.Map").Length.ShouldBe(3, "exactly two routes: the switch's read and its change");

        var frontend = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(api))!, "..", "frontend", "src");
        var files = Directory.GetFiles(Path.GetFullPath(frontend), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal)) && !f.EndsWith("schema.d.ts", StringComparison.Ordinal) && !f.Contains(".test.", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("depth", StringComparison.OrdinalIgnoreCase))
            .ToList();
        files.ShouldBeEmpty();
    }

    [Fact]
    public void The_depth_connector_asks_the_depth_allow_list_entry_and_the_live_connector_does_not()
    {
        var depth = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "DhanDepthConnector.cs"));
        depth.ShouldContain("IsAllowedDepthSocket");
        depth.ShouldNotContain("BuildFeedUri");
        var live = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "DhanFeedConnector.cs"));
        live.ShouldNotContain("Depth");
    }

    [Fact]
    public void The_depth_log_ids_are_in_their_own_range_and_the_messages_name_no_secret_or_symbol()
    {
        var methods = Application.GetType("XDrishti.Application.Depth.DepthLog")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        var messages = methods.SelectMany(m => m.GetCustomAttributes<Microsoft.Extensions.Logging.LoggerMessageAttribute>()).ToList();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(a => a.EventId >= 5301 && a.EventId <= 5330);
        foreach (var text in messages.Select(m => m.Message ?? string.Empty))
        {
            text.ShouldStartWith("depth.");
            foreach (var placeholder in new[] { "{Token", "{Client", "{Url", "{Uri", "{Symbol", "{Price", "{Security", "{Query" })
            {
                text.ShouldNotContain(placeholder, Case.Insensitive);
            }

            text.ShouldNotContain("wss://", Case.Insensitive);
        }

        // the nine events of the spec exist
        foreach (var word in new[] { "connected", "disconnected", "subscribed", "flush", "dropped", "disk_warning", "disk_stop", "token_refused", "flapping" })
        {
            messages.ShouldContain(a => (a.Message ?? string.Empty).StartsWith("depth." + word, StringComparison.Ordinal), word);
        }
    }
}
