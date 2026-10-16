using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Dividends and charges in the returns (ADR 0026, P2-14): the income calculators, the book that sorts the recorded rupees out and the views are pure — they decide from the values they are given, with no port, clock, framework or database — the
/// use cases reach the data only through ports that the infrastructure implements, and every use case is a sealed handler that is not public.
/// </summary>
public sealed class DividendsArchitectureTests
{
    private const string IncomeNamespace = "XDrishti.Application.Portfolio.Income";

    private const string DividendsNamespace = "XDrishti.Application.Portfolio.Dividends";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] Frameworks =
    [
        "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Hosting", "XDrishti.Worker",
        "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Microsoft.AspNetCore", "Npgsql", "Serilog", "System.Net.Http", "System.Data", "System.IO",
        "System.TimeProvider", "XDrishti.Application.Abstractions.Messaging", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Market",
    ];

    private static readonly string[] Ports =
    [
        typeof(IIncomeQueries).FullName!, typeof(IDividendStore).FullName!, typeof(IDividendSuggestionStore).FullName!, typeof(ITradeStore).FullName!, typeof(ILedgerStore).FullName!,
        typeof(IHoldingQueries).FullName!, typeof(IHoldingStore).FullName!, typeof(IPortfolioUnitOfWork).FullName!,
    ];

    /// <summary>Computes only: no I/O of any kind.</summary>
    private static readonly string[] Computations = ["IncomeBook", "IncomeCalculator", "IncomeViews", "IncomeFigures", "HoldingIncome", "AccountIncome", "IncomeView"];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static IEnumerable<Type> Named(string ns, IEnumerable<string> names) =>
        Application.GetTypes().Where(t => t.Namespace == ns && !t.IsNested && names.Contains(t.Name));

    [Fact]
    public void The_income_computations_do_no_io_and_reach_no_port_clock_or_framework()
    {
        Named(IncomeNamespace, Computations).Count().ShouldBe(Computations.Length);
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(IncomeNamespace).And().HaveNameMatching("^(" + string.Join('|', Computations) + ")$")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports]).GetResult());
    }

    [Fact]
    public void The_calculator_and_the_views_are_static_and_the_book_has_no_state_that_changes()
    {
        var functions = Named(IncomeNamespace, ["IncomeCalculator", "IncomeViews", "IncomeLoader"]).ToList();

        functions.Count.ShouldBe(3);
        functions.ShouldAllBe(t => t.IsAbstract && t.IsSealed);
        functions.ShouldAllBe(t => t.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => f.IsLiteral || f.IsInitOnly));
        var book = Named(IncomeNamespace, ["IncomeBook"]).Single();
        book.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ShouldAllBe(f => f.IsInitOnly);
        book.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ShouldAllBe(p => p.SetMethod == null);
    }

    [Fact]
    public void The_loader_reaches_the_data_only_through_the_income_port()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(IncomeNamespace).And().HaveName("IncomeLoader")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Ports.Where(p => p != typeof(IIncomeQueries).FullName)]).GetResult());
    }

    [Fact]
    public void The_use_cases_are_sealed_handlers_that_are_not_public()
    {
        var handlers = Application.GetTypes().Where(t => t.Namespace == DividendsNamespace && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        handlers.Count.ShouldBe(7);
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_use_cases_and_the_income_reads_reach_the_database_only_through_ports()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(DividendsNamespace).Or().ResideInNamespace(IncomeNamespace)
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api", "Microsoft.AspNetCore").GetResult());
    }

    [Fact]
    public void The_ports_are_implemented_in_the_infrastructure()
    {
        typeof(IDividendStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.DividendStore")).ShouldBeTrue();
        typeof(IDividendSuggestionStore).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.DividendSuggestionStore")).ShouldBeTrue();
        typeof(IIncomeQueries).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.IncomeQueries")).ShouldBeTrue();
    }

    [Fact]
    public void The_dividend_entities_of_the_domain_do_not_depend_on_the_application_or_the_database()
    {
        ShouldHaveNone(Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().HaveName("Dividend").Or().HaveName("DividendSuggestion").Or().HaveName("DividendRules")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions").GetResult());
    }

    [Fact]
    public void The_gross_calculators_do_not_know_about_income()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Portfolio.Returns")
            .ShouldNot().HaveDependencyOnAny(IncomeNamespace, DividendsNamespace).GetResult());
    }
}
