using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The holding groups (ADR 0021, P2-08): partitioning and the group figures are pure — the Domain, the plain data types of the Application and the pure calculators, no I/O, clock or logging — and the use case reaches the
/// master's sectors and the baskets only through a port implemented in the infrastructure.
/// </summary>
public sealed class HoldingGroupsArchitectureTests
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
        typeof(IHoldingClassifications).FullName!, typeof(IHoldingQueries).FullName!, typeof(IPortfolioPrices).FullName!, typeof(IPortfolioBenchmark).FullName!, typeof(IPortfolioHistoryCache).FullName!,
    ];

    [Fact]
    public void The_partitioning_and_group_figures_depend_on_nothing_that_does_io_tells_the_time_or_logs()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Portfolio.Groups");

        types.GetTypes().ShouldNotBeEmpty();
        var result = types.ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_partitioning_and_the_calculator_are_static_classes_with_no_state_of_their_own()
    {
        var statics = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Portfolio.Groups" && t.Name is "HoldingPartitions" or "GroupCalculator" or "GroupDimensions").ToList();

        statics.Count.ShouldBe(3);
        statics.ShouldAllBe(t => t.IsAbstract && t.IsSealed);
    }

    [Fact]
    public void The_groups_use_case_is_a_sealed_handler_that_is_not_public()
    {
        var handler = Application.GetTypes().Single(t => t.Name == "GetHoldingGroupsHandler");

        (handler.IsSealed && !handler.IsPublic).ShouldBeTrue();
    }

    [Fact]
    public void The_classification_port_is_implemented_in_the_infrastructure()
    {
        typeof(IHoldingClassifications).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.HoldingClassifications")).ShouldBeTrue();
    }

    [Fact]
    public void The_groups_use_case_does_not_reach_the_database_directly()
    {
        var result = Types.InAssembly(Application).That().HaveNameStartingWith("GetHoldingGroups").Or().HaveNameStartingWith("ClassificationLoader")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
