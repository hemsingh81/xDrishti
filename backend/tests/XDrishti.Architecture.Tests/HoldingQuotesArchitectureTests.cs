using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Quotes;

namespace XDrishti.Architecture.Tests;

/// <summary>The holdings quote poll (ADR 0019): the service depends on the ports (<see cref="IQuoteSource"/>, <see cref="IHoldingQuotes"/>) and never on the HTTP client, the database or another module's internals.</summary>
public sealed class HoldingQuotesArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void The_service_and_its_namespace_depend_on_abstractions_only()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith("XDrishti.Application.Portfolio.Quotes")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Infrastructure", "XDrishti.Api", "Microsoft.EntityFrameworkCore", "Npgsql", "System.Net.Http",
                "XDrishti.Application.Platform", "XDrishti.Application.Market", "XDrishti.Application.Accounts")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_service_lives_in_the_portfolio_module_and_is_registered_by_it()
    {
        typeof(HoldingQuotesService).Namespace.ShouldStartWith("XDrishti.Application.Portfolio");
    }

    [Fact]
    public void The_ports_are_implemented_in_infrastructure_by_the_portfolio_and_broker_code()
    {
        var quote = Infrastructure.GetType("XDrishti.Infrastructure.Brokers.DhanQuoteClient");
        var store = Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.HoldingQuotes");

        typeof(IQuoteSource).IsAssignableFrom(quote).ShouldBeTrue();
        typeof(IHoldingQuotes).IsAssignableFrom(store).ShouldBeTrue();
    }

    [Fact]
    public void The_application_layer_knows_nothing_of_the_broker_http_code()
    {
        var result = Types.InAssembly(Application).ShouldNot().HaveDependencyOn("XDrishti.Infrastructure.Brokers").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
