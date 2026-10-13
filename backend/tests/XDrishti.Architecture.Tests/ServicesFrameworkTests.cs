using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The services framework (ADR 0018) knows no particular service: the scheduler, the runner and the registry depend on the abstractions (<see cref="IScheduledService"/>, the stores) and on the
/// domain only, never on the market, accounts or portfolio modules whose work the services do, nor on infrastructure.
/// </summary>
public sealed class ServicesFrameworkTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    [Theory]
    [InlineData(typeof(ServiceScheduler))]
    [InlineData(typeof(ServiceRunner))]
    [InlineData(typeof(ServiceRegistry))]
    public void The_scheduler_the_runner_and_the_registry_depend_on_abstractions_only(Type type)
    {
        var result = Types.InAssembly(Application).That().HaveName(type.Name)
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Market", "XDrishti.Application.Accounts", "XDrishti.Application.Portfolio", "XDrishti.Application.Auth",
                "XDrishti.Infrastructure", "XDrishti.Api", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Every_service_is_a_sealed_class_registered_by_the_module_that_owns_the_work()
    {
        var services = Types.InAssembly(Application).That().ImplementInterface(typeof(IScheduledService)).GetTypes().ToList();

        services.ShouldNotBeEmpty();
        services.ShouldAllBe(t => t.IsSealed && t.IsClass);
        services.ShouldAllBe(t => !(t.Namespace ?? string.Empty).StartsWith("XDrishti.Application.Platform", StringComparison.Ordinal));
    }

    [Fact]
    public void The_whole_platform_services_namespace_knows_no_other_module_only_the_ports_it_owns()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith("XDrishti.Application.Platform.Services")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Market", "XDrishti.Application.Accounts", "XDrishti.Application.Portfolio", "XDrishti.Application.Auth",
                "XDrishti.Application.Abstractions.Portfolio", "XDrishti.Application.Abstractions.Market.Bars", "XDrishti.Infrastructure", "XDrishti.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("GetDataOverviewHandler")]
    [InlineData("GetRefreshStatusHandler")]
    public void The_screens_of_the_other_modules_read_a_services_schedule_through_the_port_not_through_the_platform_internals(string handler)
    {
        var result = Types.InAssembly(Application).That().HaveName(handler)
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Platform.Services")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_port_implementations_live_with_the_module_that_owns_the_data()
    {
        // the per-account sync is implemented by the portfolio module behind the platform's port, the schedule reader by the platform itself
        typeof(IAccountSyncSource).IsAssignableFrom(Application.GetType("XDrishti.Application.Portfolio.AccountSyncSource")).ShouldBeTrue();
        typeof(IServiceScheduleReader).IsAssignableFrom(Application.GetType("XDrishti.Application.Platform.Services.ServiceScheduleReader")).ShouldBeTrue();
    }
}
