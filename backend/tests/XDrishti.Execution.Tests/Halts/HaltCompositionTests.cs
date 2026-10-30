using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Halts;

/// <summary>P12-05 changes nothing a host can send with: the enforcer, the drill, the kill-switch coordinator and its port are composed nowhere, and the entry gate's halt source is still the closed default.</summary>
public sealed class HaltCompositionTests
{
    private static string Source(params string[] path) => File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Execution", .. path]));

    [Theory]
    [InlineData(null, null)]
    [InlineData("sandbox", "true")]
    [InlineData("live", "true")]
    public void No_environment_composes_the_enforcer_the_drill_the_kill_switch_or_a_broker_port(string? environment, string? sendEnabled)
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

        provider.GetService<HaltEnforcer>().ShouldBeNull();
        provider.GetService<DrillService>().ShouldBeNull();
        provider.GetService<KillSwitchCoordinator>().ShouldBeNull();
        provider.GetService<IBrokerKillSwitch>().ShouldBeNull();
        provider.GetService<IBrokerOrders>().ShouldBeNull();
        provider.GetService<IHaltStore>().ShouldBeNull();
        provider.GetService<IHaltSource>()!.GetType().Name.ShouldBe("ClosedHaltSource");
        provider.GetService<IHaltRequests>().ShouldBeNull();
    }

    [Fact]
    public void The_record_registration_adds_the_halt_store_and_service_but_neither_replaces_the_closed_halt_source_nor_registers_the_enforcer_the_drill_or_the_switch()
    {
        var host = Source("Host", "ExecServices.cs");

        host.ShouldContain("services.AddSingleton<IHaltStore, PgHaltStore>()");
        host.ShouldContain("services.AddSingleton<HaltService>()");
        host.ShouldContain("services.AddSingleton<IHaltSource, ClosedHaltSource>()");
        host.ShouldNotContain("IHaltSource, HaltService");
        host.ShouldNotContain("IHaltRequests");
        host.ShouldNotContain("HaltEnforcer");
        host.ShouldNotContain("DrillService");
        host.ShouldNotContain("KillSwitchCoordinator");
        host.ShouldNotContain("IBrokerKillSwitch");
    }

    [Fact]
    public void The_kill_switch_port_has_no_implementation_in_the_project_and_the_refusing_transport_is_built_only_inside_the_drill()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Execution"));
        var all = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.StartsWith(Path.Combine(root, "obj"), StringComparison.Ordinal) && !f.StartsWith(Path.Combine(root, "bin"), StringComparison.Ordinal)).ToList();
        all.Count.ShouldBeGreaterThan(50);

        all.Where(f => File.ReadAllText(f).Contains(": IBrokerKillSwitch", StringComparison.Ordinal)).ShouldBeEmpty();
        all.Where(f => File.ReadAllText(f).Contains("new RefusingBrokerOrders(", StringComparison.Ordinal)).Select(Path.GetFileName).ShouldBe(["DrillService.cs"]);
        Source("Dhan", "ExecutionEndpoints.cs").ShouldNotContain("killswitch", Case.Insensitive);
    }
}
