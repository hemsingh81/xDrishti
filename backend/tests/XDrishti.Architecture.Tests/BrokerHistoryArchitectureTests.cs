using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The trade history and ledger sync (ADR 0025, P2-13): the Dhan clients exist only in the infrastructure, behind ports; the windows, the classifier and the identity are pure; the use cases reach the database and the broker only through ports; and the
/// automatic rebuild goes through the same workspace (and so the same pure reconstructor and unit-of-work rules) as the owner's rebuild.
/// </summary>
public sealed class BrokerHistoryArchitectureTests
{
    private const string Namespace = "XDrishti.Application.Portfolio.BrokerHistory";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] Frameworks =
    [
        "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Hosting", "XDrishti.Worker",
        "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Microsoft.AspNetCore", "Npgsql", "Serilog", "System.Net.Http", "System.Data", "System.IO", "System.TimeProvider",
        "XDrishti.Application.Abstractions.Messaging", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Market", "XDrishti.Application.Abstractions.Portfolio.I",
    ];

    private static readonly string[] Computations = ["DateWindow", "BrokerWindows", "LedgerClassifier", "LedgerIdentity", "IdentifiedLedgerRow"];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_windows_the_classifier_and_the_identity_are_pure()
    {
        Application.GetTypes().Count(t => t.Namespace == Namespace && !t.IsNested && Computations.Contains(t.Name)).ShouldBe(Computations.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(Namespace).And().HaveNameMatching("^(" + string.Join('|', Computations) + ")$")
            .ShouldNot().HaveDependencyOnAny(Frameworks).GetResult());
    }

    [Fact]
    public void The_use_cases_reach_the_database_and_the_broker_only_through_ports()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(Namespace)
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api", "Microsoft.AspNetCore", "System.Net.Http", "System.Data").GetResult());
    }

    [Fact]
    public void The_clients_exist_only_in_the_infrastructure_and_implement_the_ports()
    {
        var trades = Infrastructure.GetType("XDrishti.Infrastructure.Brokers.DhanTradesClient");
        var ledger = Infrastructure.GetType("XDrishti.Infrastructure.Brokers.DhanLedgerClient");

        typeof(IBrokerTrades).IsAssignableFrom(trades).ShouldBeTrue();
        typeof(IBrokerLedger).IsAssignableFrom(ledger).ShouldBeTrue();
        foreach (var assembly in new[] { Domain, Application })
        {
            assembly.GetTypes().Where(t => !t.IsInterface && (typeof(IBrokerTrades).IsAssignableFrom(t) || typeof(IBrokerLedger).IsAssignableFrom(t))).ShouldBeEmpty();
        }

        Infrastructure.GetTypes().Where(t => !t.IsInterface && (typeof(IBrokerTrades).IsAssignableFrom(t) || typeof(IBrokerLedger).IsAssignableFrom(t))).Select(t => t.Namespace).Distinct().ShouldBe(["XDrishti.Infrastructure.Brokers"]);
    }

    [Fact]
    public void The_stores_are_implemented_in_the_infrastructure()
    {
        typeof(IBrokerSyncStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.BrokerSyncStore")).ShouldBeTrue();
        typeof(ILedgerStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.LedgerStore")).ShouldBeTrue();
    }

    [Fact]
    public void Only_the_broker_http_code_of_the_infrastructure_speaks_to_dhan()
    {
        ShouldHaveNone(Types.InAssembly(Application).ShouldNot().HaveDependencyOn("XDrishti.Infrastructure.Brokers").GetResult());
        ShouldHaveNone(Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Portfolio").ShouldNot().HaveDependencyOn("System.Net.Http").GetResult());
    }

    [Fact]
    public void The_use_cases_are_sealed_handlers_that_are_not_public()
    {
        var handlers = Application.GetTypes().Where(t => t.Namespace == Namespace && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        handlers.Count.ShouldBe(5);
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_automatic_rebuild_goes_through_the_workspace_of_the_owners_rebuild()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("BrokerLotRebuilder").Should().HaveDependencyOn("XDrishti.Application.Portfolio.Trades.TradeWorkspace").GetResult());
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(Namespace).ShouldNot().HaveDependencyOn("XDrishti.Application.Portfolio.Trades.LotReconstructor").GetResult());
    }

    [Fact]
    public void The_scheduled_service_is_registered_by_the_portfolio_module_and_runs_the_use_case_not_the_clients()
    {
        typeof(XDrishti.Application.Portfolio.BrokerHistory.PortfolioBrokerHistoryService).Namespace.ShouldStartWith("XDrishti.Application.Portfolio");
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("PortfolioBrokerHistoryService")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Portfolio.IBrokerTrades", "XDrishti.Application.Abstractions.Portfolio.IBrokerLedger", "XDrishti.Infrastructure", "System.Net.Http").GetResult());
    }
}
