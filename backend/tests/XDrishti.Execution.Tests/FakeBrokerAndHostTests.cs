using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests;

public sealed class FakeBrokerAndHostTests
{
    private static readonly BrokerLeg Leg = new("XD2610240000A1E1", LegKind.Entry, ExecSide.Buy, "11536", 1, 100m);

    [Fact]
    public async Task The_fake_broker_stores_a_repeated_client_id_once_and_looks_it_up()
    {
        var broker = new FakeBroker();
        var first = await broker.PlaceAsync(Leg, CancellationToken.None);
        var second = await broker.PlaceAsync(Leg, CancellationToken.None);

        second.BrokerOrderId.ShouldBe(first.BrokerOrderId);
        broker.Legs.Count.ShouldBe(1);
        (await broker.LookupAsync(Leg.ClientOrderId, CancellationToken.None))!.Qty.ShouldBe(1);
        (await broker.LookupAsync("nope", CancellationToken.None)).ShouldBeNull();
        (await broker.ModifyAsync(Leg.ClientOrderId, 101m, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await broker.CancelAsync(Leg.ClientOrderId, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await broker.CancelAsync(Leg.ClientOrderId, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.NotSent);
    }

    [Fact]
    public async Task A_scripted_outcome_touches_nothing_and_clears()
    {
        var broker = new FakeBroker { NextOutcome = BrokerOutcome.Unknown };

        (await broker.PlaceAsync(Leg, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Unknown);
        broker.Legs.ShouldBeEmpty();
        (await broker.PlaceAsync(Leg, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
    }

    private static ServiceCollection Services(string? environment)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(environment is null ? [] : new Dictionary<string, string?> { ["Execution:Environment"] = environment }).Build();
        var services = new ServiceCollection();
        services.AddLogging().AddExecution(config);
        return services;
    }

    private static ServiceProvider Build(string? environment) => Services(environment).BuildServiceProvider();

    [Fact]
    public void The_host_defaults_to_environment_none_has_closed_ports_and_builds_no_http_client_or_broker()
    {
        using var provider = Build(null);

        provider.GetRequiredService<IOptions<ExecOptions>>().Value.Parsed.ShouldBe(ExecEnvironment.None);
        provider.GetRequiredService<IModeSource>().ShouldNotBeNull();
        provider.GetService<IBrokerOrders>().ShouldBeNull();
        Services(null).Select(d => d.ServiceType.FullName + "|" + d.ImplementationType?.FullName).ShouldNotContain(n => n.Contains("Http", StringComparison.OrdinalIgnoreCase));
        provider.GetService<IExecSettingsSnapshots>().ShouldBeNull();
    }

    [Fact]
    public void An_unknown_environment_text_reads_as_none()
    {
        using var provider = Build("production");

        provider.GetRequiredService<IOptions<ExecOptions>>().Value.Parsed.ShouldBe(ExecEnvironment.None);
        Build("Sandbox").GetRequiredService<IOptions<ExecOptions>>().Value.Parsed.ShouldBe(ExecEnvironment.Sandbox);
    }
}
