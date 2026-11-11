using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-03b criteria 7 and 11 and the "no order" rule: the opening-minute recorder has its own connection, loop and tables. Its code references nothing of the live feed's session, subscription list or supervisor, the depth and tick recorders, the plan,
/// order or execution code; the decision layers reference none of it; the connector is the live feed's allowed connector with its own packet counts; its store only inserts.
/// </summary>
public sealed class OpeningBarsArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly string[] OpeningNamespaces = ["XDrishti.Application.OpeningBars", "XDrishti.Application.Abstractions.OpeningBars", "XDrishti.Infrastructure.OpeningBars"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    [Fact]
    public void The_opening_bars_code_touches_no_live_session_subscription_list_other_recorder_plan_or_execution_type()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith("XDrishti.Application.OpeningBars").Or().ResideInNamespaceStartingWith("XDrishti.Application.Abstractions.OpeningBars").ShouldNot().HaveDependencyOnAny(
            "XDrishti.Application.Abstractions.Live.ILiveFeedConnector",
            "XDrishti.Application.Abstractions.Live.ILiveSubscriptions",
            "XDrishti.Application.Abstractions.Live.ILiveFlush",
            "XDrishti.Application.Abstractions.Live.ILiveQuotes",
            "XDrishti.Application.Live.IFeedSession",
            "XDrishti.Application.Live.FeedConnectionManager",
            "XDrishti.Application.Live.LiveFeedSupervisor",
            "XDrishti.Application.Live.IActiveSetProvider",
            "XDrishti.Application.Live.ActiveSetLoader",
            "XDrishti.Application.Ticks",
            "XDrishti.Application.Abstractions.Ticks",
            "XDrishti.Application.Depth",
            "XDrishti.Application.Abstractions.Depth",
            "XDrishti.Application.Planning",
            "XDrishti.Application.Abstractions.Planning",
            "XDrishti.Application.Execution",
            "XDrishti.Application.Abstractions.LiveTrading",
            "XDrishti.Application.Paper",
            "XDrishti.Application.Portfolio",
            "XDrishti.Infrastructure",
            "Npgsql",
            "System.Net.WebSockets",
            "System.Net.Http").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("XDrishti.Application.Planning")]
    [InlineData("XDrishti.Application.Learning")]
    [InlineData("XDrishti.Application.Portfolio")]
    [InlineData("XDrishti.Application.Live")]
    [InlineData("XDrishti.Application.Selection")]
    [InlineData("XDrishti.Application.Research")]
    [InlineData("XDrishti.Application.Execution")]
    [InlineData("XDrishti.Application.Paper")]
    [InlineData("XDrishti.Application.Depth")]
    [InlineData("XDrishti.Application.Ticks")]
    public void The_decision_live_and_recorder_code_references_no_opening_bars_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(OpeningNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_opening_bars_infrastructure_is_referenced_only_by_its_own_code_the_host_wiring_and_the_broker_connector()
    {
        var offenders = Types.InAssembly(Infrastructure).That().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.OpeningBars").And().DoNotResideInNamespace("XDrishti.Infrastructure")
            .And().DoNotResideInNamespace("XDrishti.Infrastructure.Brokers")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.OpeningBars", "XDrishti.Application.Abstractions.OpeningBars").GetResult();

        offenders.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", offenders.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_store_only_inserts_and_the_switch_is_insert_only()
    {
        foreach (var file in new[] { "OpeningBarsStore.cs", "OpeningBarsSwitchStore.cs" })
        {
            var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "OpeningBars", file));
            text.ShouldNotContain("UPDATE ", Case.Sensitive);
            text.ShouldNotContain("DELETE ", Case.Sensitive);
            text.ShouldNotContain("TRUNCATE", Case.Sensitive);
        }

        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "OpeningBars", "OpeningBarsStore.cs")).ShouldContain("ON CONFLICT (instrument_id, minute_start) DO NOTHING");
    }

    [Fact]
    public void The_connector_is_the_live_feeds_allowed_connector_and_builds_no_url_of_its_own()
    {
        var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "OpeningBarsFeedConnector.cs"));

        text.ShouldContain("DhanFeedConnector");
        text.ShouldContain("new FeedPacketStats()");
        text.Split("new Uri(", StringSplitOptions.None).Length.ShouldBe(1);
    }

    [Fact]
    public void No_api_route_names_the_opening_bars_tables()
    {
        var api = Path.Combine(Root, "src", "XDrishti.Api");
        Directory.GetFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && File.ReadAllText(f).Contains("bars_1m_live", StringComparison.Ordinal)).ShouldBeEmpty();
    }
}
