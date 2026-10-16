using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Dividend file import (ADR 0028, P2-15): the file-kind detector, the reader, the name normaliser and matcher, the identity and the planner are pure — they decide from the text or the values they are given, with no port, clock, framework or database — the
/// use cases reach the data only through ports that the infrastructure implements, and every use case is a sealed handler that is not public. The trade code knows the dividend import only through the file-kind detector.
/// </summary>
public sealed class DividendImportArchitectureTests
{
    private const string ImportNamespace = "XDrishti.Application.Portfolio.DividendImport";

    private const string KindsNamespace = "XDrishti.Application.Portfolio.Imports";

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
        typeof(IDividendStore).FullName!, typeof(IDividendBatches).FullName!, typeof(IDividendAliases).FullName!, typeof(IInstrumentNames).FullName!, typeof(IHoldingQueries).FullName!,
        typeof(IHoldingStore).FullName!, typeof(IPortfolioUnitOfWork).FullName!, typeof(ITradeStore).FullName!,
    ];

    /// <summary>Computes only: no I/O of any kind.</summary>
    private static readonly string[] Computations =
    [
        "DividendCells", "DividendNames", "DividendColumns", "DividendIdentity", "DividendMatch", "DividendNameIndex", "DividendPlanner", "DividendPlan", "DividendStatuses", "DividendImportViews",
        "ParsedDividend", "DividendFile", "PlannedDividend", "SelectedDividend", "AliasToSave", "DividendSelection", "IdentifiedDividend",
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static IEnumerable<Type> Named(string ns, IEnumerable<string> names) =>
        Application.GetTypes().Where(t => t.Namespace == ns && !t.IsNested && names.Contains(t.Name));

    [Fact]
    public void The_computations_do_no_io_and_reach_no_port_clock_or_framework()
    {
        Named(ImportNamespace, Computations).Count().ShouldBe(Computations.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(ImportNamespace).And().HaveNameMatching("^(" + string.Join('|', Computations) + ")$")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports, "System.IO"]).GetResult());
    }

    [Fact]
    public void The_file_kind_detector_looks_at_header_names_only()
    {
        Named(KindsNamespace, ["FileKindDetector", "FileKind"]).Count().ShouldBe(2);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(KindsNamespace).ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports, "System.IO"]).GetResult());
    }

    [Fact]
    public void The_reader_reads_only_the_stream_it_is_given()
    {
        Named(ImportNamespace, ["DividendCsvReader"]).Count().ShouldBe(1);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(ImportNamespace).And().HaveName("DividendCsvReader").ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports]).GetResult());
    }

    [Fact]
    public void The_pure_types_are_static_with_no_state_of_their_own_and_the_index_keeps_only_what_it_was_built_from()
    {
        var functions = Named(ImportNamespace, ["DividendCells", "DividendNames", "DividendIdentity", "DividendPlanner", "DividendCsvReader", "DividendImportViews", "DividendStatuses"]).Concat(Named(KindsNamespace, ["FileKindDetector"])).ToList();

        functions.Count.ShouldBe(8);
        functions.ShouldAllBe(t => t.IsAbstract && t.IsSealed);
        functions.ShouldAllBe(t => t.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => f.IsLiteral || f.IsInitOnly));
        var index = Named(ImportNamespace, ["DividendNameIndex"]).Single();
        index.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => f.Name != "_answers").ShouldAllBe(f => f.IsInitOnly);
        index.IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void The_use_cases_are_sealed_handlers_that_are_not_public()
    {
        var handlers = Application.GetTypes().Where(t => t.Namespace == ImportNamespace && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        handlers.Count.ShouldBe(4);
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_use_cases_reach_the_database_only_through_ports()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(ImportNamespace).Or().ResideInNamespace(KindsNamespace)
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api", "Microsoft.AspNetCore").GetResult());
    }

    [Fact]
    public void The_trade_code_knows_the_dividend_import_only_through_the_file_kind_detector()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(TradesNamespace).ShouldNot().HaveDependencyOn(ImportNamespace).GetResult());
    }

    [Fact]
    public void The_ports_are_implemented_in_the_infrastructure()
    {
        typeof(IDividendStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.DividendStore")).ShouldBeTrue();
        typeof(IDividendBatches).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.DividendBatches")).ShouldBeTrue();
        typeof(IDividendAliases).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.DividendAliases")).ShouldBeTrue();
        typeof(IInstrumentNames).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.InstrumentNames")).ShouldBeTrue();
    }

    [Fact]
    public void The_dividend_import_entities_of_the_domain_do_not_depend_on_the_application_or_the_database()
    {
        ShouldHaveNone(Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().HaveName("DividendBatch").Or().HaveName("DividendAlias").Or().HaveName("Dividend").Or().HaveName("DividendRules")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions").GetResult());
    }
}
