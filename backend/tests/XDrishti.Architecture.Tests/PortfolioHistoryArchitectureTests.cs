using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The portfolio value history (ADR 0020, P2-07): the calculators are pure — they know the Domain and the plain data types of the Application and nothing that does I/O, tells the time or writes logs — and the use cases reach the
/// stored bars, the benchmark setting and the cache only through ports implemented in the infrastructure.
/// </summary>
public sealed class PortfolioHistoryArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] Forbidden =
    [
        "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Hosting", "XDrishti.Worker",
        "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Microsoft.AspNetCore", "Npgsql", "Serilog",
        "System.Net.Http", "System.IO", "System.Data",
        "System.TimeProvider",
        "XDrishti.Application.Abstractions.Messaging", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Market",
        typeof(IPortfolioPrices).FullName!, typeof(IPortfolioBenchmark).FullName!, typeof(IPortfolioHistoryCache).FullName!, typeof(IHoldingQueries).FullName!,
    ];

    [Fact]
    public void The_calculators_depend_on_nothing_outside_the_domain_and_the_plain_application_types()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Portfolio.Returns");

        types.GetTypes().ShouldNotBeEmpty();
        var result = types.ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_calculators_are_static_or_plain_data_with_no_state_of_their_own()
    {
        var calculators = new[] { typeof(PortfolioHistoryCalculator), typeof(NavSeries), typeof(DrawdownCalculator), typeof(BenchmarkReturns), typeof(AdjustedCloses), typeof(HistoryResolutions), typeof(Xirr) };

        calculators.ShouldAllBe(t => t.IsAbstract && t.IsSealed); // static classes
    }

    [Fact]
    public void The_use_cases_are_registered_handlers_that_are_sealed_and_not_public()
    {
        var handlers = new[]
        {
            typeof(GetPortfolioHistoryQuery), typeof(GetBenchmarkQuery), typeof(SetBenchmarkCommand), typeof(TrackPortfolioHoldingsCommand), typeof(TrackBenchmarkCommand),
        }.Select(request => Application.GetTypes().Single(t => t.Name == request.Name.Replace("Query", "Handler", StringComparison.Ordinal).Replace("Command", "Handler", StringComparison.Ordinal)));

        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_ports_are_implemented_in_the_infrastructure()
    {
        typeof(IPortfolioPrices).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.PortfolioPrices")).ShouldBeTrue();
        typeof(IPortfolioBenchmark).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.PortfolioBenchmarkStore")).ShouldBeTrue();
        typeof(IPortfolioHistoryCache).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.PortfolioHistoryCache")).ShouldBeTrue();
    }

    [Fact]
    public void The_history_use_cases_do_not_reach_the_database_or_the_cache_implementation_directly()
    {
        var result = Types.InAssembly(Application).That().HaveNameStartingWith("GetPortfolioHistory").Or().HaveNameStartingWith("SetBenchmark").Or().HaveNameStartingWith("TrackBenchmark").Or().HaveNameStartingWith("TrackPortfolioHoldings")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "System.Collections.Concurrent")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
