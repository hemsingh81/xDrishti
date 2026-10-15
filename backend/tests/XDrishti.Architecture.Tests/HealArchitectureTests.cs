using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The self-healing syncs and the attention items (ADR 0027, P2-16): the rules about when to retry, what a failure means and which items to raise are pure; the code that decides reaches the database only through ports; the stores are in the infrastructure; the healing
/// starts runs only through the same use cases as "Sync now" and the schedules; the Dhan mappers carry their version.
/// </summary>
public sealed class HealArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] Namespaces = ["XDrishti.Application.Portfolio.Heal", "XDrishti.Application.Portfolio.Attention"];

    private static readonly string[] Pure = ["RetrySchedule", "SyncFailures", "AttentionDeriver", "AttentionCommands", "DropReasonWords"];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_retry_schedule_the_failure_codes_and_the_item_rules_are_pure()
    {
        Application.GetTypes().Count(t => Namespaces.Contains(t.Namespace) && !t.IsNested && Pure.Contains(t.Name)).ShouldBe(Pure.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().HaveNameMatching("^(" + string.Join('|', Pure) + ")$")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Infrastructure", "XDrishti.Api", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Npgsql", "System.Net.Http", "System.Data", "System.IO", "System.TimeProvider",
                "XDrishti.Application.Abstractions.Portfolio.ISyncHealthStore", "XDrishti.Application.Abstractions.Portfolio.IAttentionStore", "XDrishti.Application.Abstractions.Portfolio.IAttentionQueries",
                "XDrishti.Application.Abstractions.Portfolio.IBrokerSyncStore", "XDrishti.Application.Abstractions.Messaging").GetResult());
    }

    [Fact]
    public void The_healing_and_the_items_reach_the_database_and_the_broker_only_through_ports()
    {
        foreach (var ns in Namespaces)
        {
            ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(ns)
                .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api", "Microsoft.AspNetCore", "System.Net.Http", "System.Data").GetResult());
        }
    }

    [Fact]
    public void The_stores_and_the_settings_are_implemented_in_the_infrastructure()
    {
        typeof(ISyncHealthStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.SyncHealthStore")).ShouldBeTrue();
        typeof(IAttentionStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.AttentionStore")).ShouldBeTrue();
        typeof(IAttentionQueries).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.AttentionQueries")).ShouldBeTrue();
        typeof(IHealSettings).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.HealSettings")).ShouldBeTrue();
        foreach (var port in new[] { typeof(ISyncHealthStore), typeof(IAttentionStore), typeof(IAttentionQueries), typeof(IHealSettings) })
        {
            Application.GetTypes().Where(t => !t.IsInterface && port.IsAssignableFrom(t)).ShouldBeEmpty();
        }
    }

    [Fact]
    public void The_healer_and_the_recorder_are_internal_and_the_healer_starts_runs_only_through_the_use_cases()
    {
        foreach (var name in new[] { "PortfolioHealer", "SyncHealthRecorder" })
        {
            var type = Application.GetType("XDrishti.Application.Portfolio.Heal." + name);
            type.ShouldNotBeNull();
            (type.IsSealed && !type.IsPublic).ShouldBeTrue(name);
        }

        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("PortfolioHealer")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Portfolio.BrokerHistory.BrokerHistoryPipeline", "XDrishti.Application.Portfolio.BrokerHistory.BrokerLotRebuilder", "XDrishti.Application.Portfolio.SyncRuns",
                "XDrishti.Application.Abstractions.Portfolio.IBrokerSyncStore", "XDrishti.Application.Abstractions.Portfolio.IBrokerTrades", "XDrishti.Application.Abstractions.Portfolio.IBrokerHoldings").GetResult());
    }

    [Fact]
    public void The_notices_derive_the_items_in_one_place_from_set_based_facts()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("AttentionFactsLoader").ShouldNot().HaveDependencyOnAny("XDrishti.Application.Portfolio.BrokerHistory.BrokerHistoryPipeline").GetResult());
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("AttentionReconciler").Should().HaveDependencyOn("XDrishti.Application.Portfolio.Attention.AttentionDeriver").GetResult());
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("AttentionDeriver").Should().HaveDependencyOn("XDrishti.Application.Portfolio.Attention.AttentionCommands").GetResult());
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("AttentionDeriver").ShouldNot().HaveDependencyOn("XDrishti.Application.Portfolio.GetPortfolioNoticesHandler").GetResult());
    }

    [Fact]
    public void The_notices_only_read_the_attention_items_and_the_reconciler_is_their_one_writer()
    {
        // ADR 0030: the query reads what was stored; deriving and writing belong to the reconciler (the healer tick, the end of a sync run).
        ShouldHaveNone(Types.InAssembly(Application).That().HaveName("GetPortfolioNoticesHandler")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Portfolio.Attention.AttentionDeriver", "XDrishti.Application.Portfolio.Attention.IAttentionFactsSource", "XDrishti.Application.Portfolio.Attention.IAttentionReconciler",
                "XDrishti.Application.Portfolio.Attention.AttentionFactsLoader", "XDrishti.Application.Portfolio.Attention.AttentionReconciler").GetResult());
        Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Portfolio.Attention" && !t.IsInterface && typeof(IAttentionReconciler).IsAssignableFrom(t)).Select(t => t.Name).ShouldBe(["AttentionReconciler"]);
        var reconciler = Application.GetType("XDrishti.Application.Portfolio.Attention.AttentionReconciler");
        reconciler.ShouldNotBeNull();
        (reconciler.IsSealed && !reconciler.IsPublic).ShouldBeTrue();
    }

    [Fact]
    public void Each_dhan_mapper_carries_a_version_the_client_reports_through_its_port()
    {
        foreach (var (mapper, client, port) in new[] { ("DhanTradeMapper", "DhanTradesClient", typeof(IBrokerTrades)), ("DhanLedgerMapper", "DhanLedgerClient", typeof(IBrokerLedger)) })
        {
            var version = Infrastructure.GetType("XDrishti.Infrastructure.Brokers." + mapper)!.GetField("Version", BindingFlags.Public | BindingFlags.Static)!;
            ((int)version.GetValue(null)!).ShouldBeGreaterThanOrEqualTo(1);
            port.IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Brokers." + client)).ShouldBeTrue();
            port.GetProperty("MapperVersion").ShouldNotBeNull();
        }
    }
}
