using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Architecture.Tests;

/// <summary>The live price sink (ADR 0024): it depends on ports only, the Portfolio module implements the write of a holding's price, and the ports are implemented in the layer and module that own the data.</summary>
public sealed class LiveFeedArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void The_sink_and_its_namespace_depend_on_abstractions_only()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith("XDrishti.Application.Portfolio.Live")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Infrastructure", "XDrishti.Api", "Microsoft.EntityFrameworkCore", "Npgsql", "System.Net",
                "XDrishti.Application.Platform", "XDrishti.Application.Market", "XDrishti.Application.Accounts")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_sink_lives_in_the_portfolio_module()
    {
        var sink = Application.GetType("XDrishti.Application.Portfolio.Live.LiveFlush").ShouldNotBeNull();

        sink.IsSealed.ShouldBeTrue();
        sink.IsNotPublic.ShouldBeTrue();
        typeof(ILiveFlush).IsAssignableFrom(sink).ShouldBeTrue();
    }

    [Fact]
    public void The_holding_price_write_is_implemented_by_the_portfolio_module_and_the_live_quotes_by_the_market_module()
    {
        Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.LivePrices").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILivePrices));
        Infrastructure.GetType("XDrishti.Infrastructure.Market.Live.LiveQuotes").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILiveQuotes));
        Infrastructure.GetType("XDrishti.Infrastructure.Platform.LiveNotifier").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILiveNotifier));
    }

    // ---- the feed itself (P2-10 stage 2) ----

    private static readonly Assembly Feed = typeof(XDrishti.Feed.LiveOptions).Assembly;

    [Fact]
    public void The_feed_host_references_application_infrastructure_and_hosting_only()
    {
        var referenced = Feed.GetReferencedAssemblies().Select(a => a.Name).Where(n => n is not null && n.StartsWith("XDrishti.", StringComparison.Ordinal)).ToList();

        referenced.ShouldNotContain("XDrishti.Api");
        referenced.ShouldNotContain("XDrishti.Worker");
        referenced.ShouldNotContain("XDrishti.Domain");
        referenced.ShouldBeSubsetOf(["XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting"]);
    }

    [Fact]
    public void The_feed_logic_in_the_application_layer_knows_no_socket_database_or_http()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith("XDrishti.Application.Live")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Feed", "Microsoft.EntityFrameworkCore", "Npgsql", "System.Net.WebSockets", "System.Net.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void No_type_of_the_application_layer_opens_a_web_socket()
    {
        var result = Types.InAssembly(Application).ShouldNot().HaveDependencyOn("System.Net.WebSockets").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Only_the_dhan_feed_client_opens_a_web_socket_and_it_checks_the_allow_list()
    {
        var sockets = Types.InAssembly(Infrastructure).That().HaveDependencyOn("System.Net.WebSockets.ClientWebSocket").GetTypes().Select(t => t.Name).ToList();
        var checking = Types.InAssembly(Infrastructure).That().HaveNameStartingWith("DhanFeedConnector").And().HaveDependencyOn("XDrishti.Infrastructure.Brokers.BrokerEndpoints").GetTypes().ToList();

        sockets.ShouldBe(["DhanFeedConnector", "DhanFeedConnection"], ignoreOrder: true);
        checking.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_feed_engine_is_internal_and_the_host_reaches_it_through_the_supervisor_port()
    {
        foreach (var name in new[] { "FeedConnectionManager", "LiveFeedSupervisor", "ActiveSetLoader", "ActiveSetBuilder", "HoldingsProvider", "AlertsProvider", "IndicesProvider" })
        {
            var type = Application.GetType("XDrishti.Application.Live." + name).ShouldNotBeNull();
            type.IsNotPublic.ShouldBeTrue(name + " is part of the engine, not of the surface");
        }

        Application.GetType("XDrishti.Application.Live.LiveFeedSupervisor")!.GetInterfaces().ShouldContain(typeof(IFeedSupervisor));
        Feed.GetTypes().Where(t => t.IsPublic).Select(t => t.Name).ShouldBe(["LiveOptions"]);
    }

    [Fact]
    public void The_adapters_of_the_feed_are_in_the_layer_and_module_that_own_the_data()
    {
        Infrastructure.GetType("XDrishti.Infrastructure.Brokers.DhanFeedConnector").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILiveFeedConnector));
        Infrastructure.GetType("XDrishti.Infrastructure.Brokers.DhanFeedConnection").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILiveFeedConnection));
        Infrastructure.GetType("XDrishti.Infrastructure.Platform.ActiveSetChanges").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(IActiveSetChanges));
        Infrastructure.GetType("XDrishti.Infrastructure.Market.Live.IndexSecurities").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(IIndexSecurities));
        Infrastructure.GetType("XDrishti.Infrastructure.Platform.FeedManualRun").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(IFeedManualRun));
    }
}
