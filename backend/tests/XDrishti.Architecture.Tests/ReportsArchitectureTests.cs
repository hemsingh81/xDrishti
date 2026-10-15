using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Dhan's downloaded reports (ADR 0029, P2-17): the readers, the name matcher, the planners, the identity and the views are pure - they decide from the rows or the values they are given, with no port, clock, framework or file - the use cases reach the data only through
/// ports that the infrastructure implements, spreadsheets are read by a library that only the infrastructure knows, and every use case is a sealed handler that is not public. The trade code, the dividend import and the file-kind detector know nothing of the report import.
/// </summary>
public sealed class ReportsArchitectureTests
{
    private const string ReportsNamespace = "XDrishti.Application.Portfolio.Reports";

    private const string KindsNamespace = "XDrishti.Application.Portfolio.Imports";

    private const string TradesNamespace = "XDrishti.Application.Portfolio.Trades";

    private const string DividendNamespace = "XDrishti.Application.Portfolio.DividendImport";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] Frameworks =
    [
        "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Hosting", "XDrishti.Worker",
        "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Microsoft.AspNetCore", "Npgsql", "Serilog", "System.Net.Http", "System.Data", "ExcelDataReader",
        "System.TimeProvider", "System.Security.Cryptography.RandomNumberGenerator",
        "XDrishti.Application.Abstractions.Messaging", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Market",
    ];

    private static readonly string[] Ports =
    [
        typeof(ISpreadsheetReader).FullName!, typeof(ISecurityAliases).FullName!, typeof(ICostBatches).FullName!, typeof(ILedgerStore).FullName!, typeof(ITradeStore).FullName!,
        typeof(IHoldingQueries).FullName!, typeof(IInstrumentNames).FullName!, typeof(IDividendAliases).FullName!, typeof(IPortfolioUnitOfWork).FullName!, typeof(ITradeBatches).FullName!,
    ];

    /// <summary>Computes only: no I/O of any kind, no port, no clock.</summary>
    private static readonly string[] Computations =
    [
        "Sheet", "SheetRow", "ReportCells", "HeaderMap", "ReportText", "GtrRow", "GtrFile", "GlobalTransactionReader", "DpLine", "DpFile", "DpChargesReader", "DematRow", "DematFile", "DematHoldingReader",
        "BillName", "SecurityRef", "SecurityMatch", "SecurityIndex", "BillLeg", "BillTrades", "BillNameItem", "CoverageDifference", "BillPlan", "BillPlanner", "BillChoices", "AppliedChoices",
        "PlannedDp", "DpPlan", "DpPlanner", "DematComparison", "DematChecker", "ReportViews", "CostViews", "ReportErrors", "TaughtName",
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static IEnumerable<Type> Named(string ns, IEnumerable<string> names) =>
        Application.GetTypes().Where(t => t.Namespace == ns && !t.IsNested && names.Contains(t.Name));

    [Fact]
    public void The_computations_do_no_io_and_reach_no_port_clock_or_framework()
    {
        Named(ReportsNamespace, Computations).Count().ShouldBe(Computations.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(ReportsNamespace).And().HaveNameMatching("^(" + string.Join('|', Computations) + ")$")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports, "System.IO"]).GetResult());
    }

    [Fact]
    public void The_pure_functions_are_static_with_no_state_of_their_own_and_the_index_keeps_only_what_it_was_built_from()
    {
        var functions = Named(ReportsNamespace, ["ReportCells", "ReportText", "GlobalTransactionReader", "DpChargesReader", "DematHoldingReader", "BillTrades", "BillPlanner", "BillChoices", "DpPlanner", "DematChecker", "ReportViews", "CostViews", "ReportErrors"]).ToList();

        functions.Count.ShouldBe(13);
        functions.ShouldAllBe(t => t.IsAbstract && t.IsSealed);
        functions.ShouldAllBe(t => t.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => f.IsLiteral || f.IsInitOnly));
        var index = Named(ReportsNamespace, ["SecurityIndex"]).Single();
        index.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => f.Name is not ("_answers" or "_choices")).ShouldAllBe(f => f.IsInitOnly);
        index.IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void The_use_cases_are_sealed_handlers_that_are_not_public()
    {
        var handlers = Application.GetTypes().Where(t => t.Namespace == ReportsNamespace && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        handlers.Count.ShouldBe(4);
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_use_cases_reach_the_database_and_the_files_only_through_ports()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(ReportsNamespace)
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api", "Microsoft.AspNetCore", "ExcelDataReader").GetResult());
    }

    [Fact]
    public void The_trade_code_the_dividend_import_and_the_detector_know_nothing_of_the_report_import()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(TradesNamespace).Or().ResideInNamespace(DividendNamespace).Or().ResideInNamespace(KindsNamespace)
            .ShouldNot().HaveDependencyOn(ReportsNamespace).GetResult());
    }

    [Fact]
    public void The_detector_stays_a_look_at_titles_and_header_names()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(KindsNamespace).ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports, "System.IO"]).GetResult());
    }

    [Fact]
    public void Only_the_infrastructure_knows_the_spreadsheet_library()
    {
        Application.GetReferencedAssemblies().ShouldNotContain(a => a.Name == "ExcelDataReader");
        Domain.GetReferencedAssemblies().ShouldNotContain(a => a.Name == "ExcelDataReader");
        Infrastructure.GetReferencedAssemblies().ShouldContain(a => a.Name == "ExcelDataReader");
    }

    [Fact]
    public void The_ports_are_implemented_in_the_infrastructure()
    {
        typeof(ISpreadsheetReader).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.SpreadsheetReader")).ShouldBeTrue();
        typeof(ISecurityAliases).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.SecurityAliases")).ShouldBeTrue();
        typeof(ICostBatches).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.CostBatches")).ShouldBeTrue();
    }

    [Fact]
    public void The_entities_of_the_domain_do_not_depend_on_the_application_or_the_database()
    {
        ShouldHaveNone(Types.InAssembly(Domain).That().HaveName("CostBatch").Or().HaveName("SecurityAlias").Or().HaveName("LedgerEntry").Or().HaveName("Transaction")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "ExcelDataReader").GetResult());
    }
}
