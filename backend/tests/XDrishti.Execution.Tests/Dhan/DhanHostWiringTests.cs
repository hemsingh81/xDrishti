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
    public async Task The_sandbox_registers_the_adapter_but_without_secret_files_it_refuses_locally_and_nothing_can_send()
    {
        using var provider = Build("sandbox");

        var broker = provider.GetRequiredService<IBrokerOrders>();
        broker.ShouldBeOfType<DhanOrderClient>();
        var cid = ClientOrderId.Create(new DateOnly(2026, 10, 26), 1, LegKind.Entry, 1).Value.ToString();
        (await broker.PlaceAsync(new BrokerLeg(cid, LegKind.Entry, ExecSide.Buy, "1594", 1, 100m), CancellationToken.None)).Reason.ShouldBe("execution.token_unavailable");
        provider.GetService<LegSender>().ShouldBeNull("the send protocol is not composed by the host in this slice");
        provider.GetService<ISendJournal>().ShouldBeNull();
        (await provider.GetRequiredService<IModeSource>().AnswerAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void The_sandbox_credentials_come_from_the_secret_file_keys_and_blank_means_unavailable()
    {
        using var empty = Build("sandbox");
        empty.GetRequiredService<IExecCredentials>().Current().ShouldBeNull();

        using var set = Build("sandbox", new() { ["Execution:Sandbox:ClientId"] = "1000000001", ["Execution:Sandbox:Token"] = "t" });
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
