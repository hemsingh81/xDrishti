using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Trade import and lot reconstruction (ADR 0023, P2-03): the reader, the reconstruction and the planner are pure — they decide from the text or the values they are given, with no port, clock, framework or database — the use cases reach the
/// trades only through ports that the infrastructure implements, and every use case is a sealed handler that is not public.
/// </summary>
public sealed class TradesArchitectureTests
{
    private const string TradesNamespace = "XDrishti.Application.Portfolio.Trades";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] Frameworks =
    [
        "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Hosting", "XDrishti.Worker",
        "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Microsoft.AspNetCore", "Npgsql", "Serilog", "System.Net.Http", "System.Data",
        "System.TimeProvider", "System.Security.Cryptography.RandomNumberGenerator",
        "XDrishti.Application.Abstractions.Messaging", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Market",
    ];

    private static readonly string[] Ports =
    [
        typeof(ITradeStore).FullName!, typeof(ITradeBatches).FullName!, typeof(ITradeReferenceData).FullName!, typeof(IHoldingStore).FullName!, typeof(IHoldingQueries).FullName!,
        typeof(IHoldingSyncStore).FullName!, typeof(IPortfolioUnitOfWork).FullName!, typeof(IPortfolioHistoryCache).FullName!,
        "XDrishti.Application.Abstractions.Accounts",
    ];

    /// <summary>Computes only: no I/O of any kind.</summary>
    private static readonly string[] Computations =
    [
        "TradeCells", "TradeSkips", "TradeColumns", "TradeIdentity", "ActionConversion", "LotReconstructor", "LotShapes", "HoldingMatcher", "HoldingRebuilds", "TradeRebuildPlanner", "TradeInstruments",
        "ReconTrade", "Reconstruction", "HoldingRebuild", "ParsedTrade", "TradeFile", "TradeTemplate",
    ];

    /// <summary>Reads the stream it is given, nothing else.</summary>
    private static readonly string[] Readers = ["TradeCsvReader", "LineReader"];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static IEnumerable<Type> Named(IEnumerable<string> names) =>
        Application.GetTypes().Where(t => t.Namespace == TradesNamespace && !t.IsNested && names.Contains(t.Name));

    [Fact]
    public void The_computations_do_no_io_and_reach_no_port_clock_or_framework()
    {
        var types = Named(Computations).ToList();

        types.Count.ShouldBe(Computations.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(TradesNamespace).And().HaveNameMatching("^(" + string.Join('|', Computations) + ")$")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports, "System.IO"]).GetResult());
    }

    [Fact]
    public void The_reader_reads_only_the_stream_it_is_given()
    {
        Named(Readers).Count().ShouldBe(Readers.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(TradesNamespace).And().HaveNameMatching("^(" + string.Join('|', Readers) + ")$")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports]).GetResult());
    }

    [Fact]
    public void The_pure_types_are_static_or_immutable_values_with_no_state_of_their_own()
    {
        var functions = Named(["TradeCells", "TradeSkips", "TradeIdentity", "ActionConversion", "LotReconstructor", "LotShapes", "HoldingRebuilds", "TradeRebuildPlanner", "TradeCsvReader"]).ToList();

        functions.Count.ShouldBe(9);
        functions.ShouldAllBe(t => t.IsAbstract && t.IsSealed);
        functions.ShouldAllBe(t => t.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => f.IsLiteral || f.IsInitOnly));
    }

    [Fact]
    public void The_use_cases_are_sealed_handlers_that_are_not_public()
    {
        var handlers = Application.GetTypes().Where(t => t.Namespace == TradesNamespace && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        handlers.Count.ShouldBe(9);
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_use_cases_reach_the_database_only_through_ports()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(TradesNamespace)
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api", "Microsoft.AspNetCore").GetResult());
    }

    [Fact]
    public void The_ports_are_implemented_in_the_infrastructure()
    {
        typeof(ITradeStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.TradeStore")).ShouldBeTrue();
        typeof(ITradeBatches).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.TradeBatches")).ShouldBeTrue();
        typeof(ITradeReferenceData).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.TradeReferenceData")).ShouldBeTrue();
    }

    [Fact]
    public void The_trade_entities_of_the_domain_do_not_depend_on_the_application_or_the_database()
    {
        ShouldHaveNone(Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().HaveName("Transaction").Or().HaveName("TradeBatch").Or().HaveName("TradeRules").Or().HaveName("LotPlan")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions").GetResult());
    }
}
