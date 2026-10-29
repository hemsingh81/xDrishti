using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>P12-04 changes nothing a host composes: the protection services, the exit policy, the stop port and the halt stub are registered nowhere.</summary>
public sealed class ProtectionCompositionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("none", null)]
    [InlineData("live", "true")]
    [InlineData("sandbox", null)]
    [InlineData("sandbox", "true")]
    public void No_host_configuration_composes_the_protection_services_or_a_broker_port(string? environment, string? sendEnabled)
    {
        var values = new Dictionary<string, string?>();
        if (environment is not null)
        {
            values["Execution:Environment"] = environment;
        }

        if (sendEnabled is not null)
        {
            values["Execution:SendEnabled"] = sendEnabled;
        }

        using var provider = new ServiceCollection().AddLogging().AddExecution(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

        provider.GetService<ProtectionWatchdog>().ShouldBeNull();
        provider.GetService<StopManager>().ShouldBeNull();
        provider.GetService<SquareOffService>().ShouldBeNull();
        provider.GetService<ProtectionExitPolicy>().ShouldBeNull();
        (provider.GetService<IExitSendPolicy>()?.GetType().Name ?? "ClosedExitSendPolicy").ShouldBe("ClosedExitSendPolicy", "the exit policy is the closed default or absent");
        provider.GetService<IBrokerStops>().ShouldBeNull();
        provider.GetService<IBrokerOrders>().ShouldBeNull();
        provider.GetService<IHaltRequests>().ShouldBeNull();
        provider.GetService<IProtectionSource>().ShouldBeNull();
        provider.GetService<IAlarmStore>().ShouldBeNull();
    }

    [Fact]
    public void The_exit_policy_default_stays_the_closed_one_in_the_source()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Execution", "Application", "Ports", "IExitSendPolicy.cs"));

        source.ShouldContain("new GateDecision(false, \"no_exit_policy\")");
    }
}
