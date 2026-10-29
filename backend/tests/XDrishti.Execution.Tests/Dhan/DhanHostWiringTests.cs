using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Dhan;

public sealed class DhanHostWiringTests
{
    private static ServiceProvider Build(string? environment, Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>(extra ?? []);
        if (environment is not null)
        {
            values["Execution:Environment"] = environment;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddExecution(config).BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    [InlineData("live")]
    [InlineData("Live")]
    [InlineData("production")]
    [InlineData("")]
    public void Only_the_sandbox_environment_registers_the_adapter(string? environment)
    {
        using var provider = Build(environment);

        provider.GetService<IBrokerOrders>().ShouldBeNull();
        provider.GetService<IExecCredentials>().ShouldBeNull();
        provider.GetService<LegSender>().ShouldBeNull();
    }

    [Fact]
    public void The_sandbox_alone_composes_nothing_the_send_switch_is_a_second_explicit_default_off_setting()
    {
        using var provider = Build("sandbox");

        provider.GetService<IBrokerOrders>().ShouldBeNull();
        provider.GetService<IBrokerReads>().ShouldBeNull();
        provider.GetService<LegSender>().ShouldBeNull();
        provider.GetService<LegLookup>().ShouldBeNull();
        provider.GetService<IExecCredentials>().ShouldBeNull();
        foreach (var off in new[] { "false", "", "no", "1", "on" })
        {
            using var other = Build("sandbox", new() { ["Execution:SendEnabled"] = off });
            other.GetService<LegSender>().ShouldBeNull(off);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    [InlineData("live")]
    [InlineData("Live")]
    [InlineData("production")]
    public void The_send_switch_does_nothing_outside_the_sandbox_environment(string? environment)
    {
        using var provider = Build(environment, new() { ["Execution:SendEnabled"] = "true" });

        provider.GetService<LegSender>().ShouldBeNull();
        provider.GetService<LegLookup>().ShouldBeNull();
        provider.GetService<IBrokerReads>().ShouldBeNull();
        provider.GetService<IBrokerOrders>().ShouldBeNull();
    }

    [Fact]
    public void With_sandbox_and_the_switch_only_the_sender_is_the_order_surface_and_it_needs_the_persistent_record()
    {
        var services = new ServiceCollection().AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Execution:Environment"] = "sandbox", ["Execution:SendEnabled"] = "true" }).Build();
        services.AddExecution(config);

        services.ShouldContain(d => d.ServiceType == typeof(LegSender));
        services.ShouldNotContain(d => d.ServiceType == typeof(IBrokerOrders), "the raw adapter is never a service");
        services.ShouldNotContain(d => d.ServiceType == typeof(DhanOrderClient));
        services.ShouldNotContain(d => d.ImplementationType == typeof(DhanOrderClient));
        using var provider = services.BuildServiceProvider();
        provider.GetService<IBrokerOrders>().ShouldBeNull();
        var reads = provider.GetRequiredService<IBrokerReads>();
        reads.ShouldNotBeOfType<DhanOrderClient>();
        reads.ShouldNotBeAssignableTo<IBrokerOrders>();
        provider.GetRequiredService<IExecCredentials>().Current().ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<LegSender>(), "without the persistent journal the sender cannot be built: no audit-less path");
    }

    [Fact]
    public void The_sandbox_credentials_come_from_the_secret_file_keys_and_blank_means_unavailable()
    {
        using var empty = Build("sandbox", new() { ["Execution:SendEnabled"] = "true" });
        empty.GetRequiredService<IExecCredentials>().Current().ShouldBeNull();

        using var set = Build("sandbox", new() { ["Execution:SendEnabled"] = "true", ["Execution:Sandbox:ClientId"] = "1000000001", ["Execution:Sandbox:Token"] = "t" });
        set.GetRequiredService<IExecCredentials>().Current().ShouldNotBeNull();
    }

    [Fact]
    public void The_new_settings_have_the_spec_ranges_and_defaults()
    {
        ExecSettingRegistry.Find("exec.exit_collar_pct").ShouldNotBeNull().Default.ShouldBe("1.0");
        ExecSettingRegistry.Find("exec.send_timeout_s").ShouldNotBeNull().Default.ShouldBe("10");
        ExecSettingRegistry.Find("exec.order_valid_for_s").ShouldNotBeNull().Default.ShouldBe("5");
        ExecSettingRegistry.IsValid(ExecSettingRegistry.Find("exec.send_timeout_s")!, "31").ShouldBeFalse();
    }
}
