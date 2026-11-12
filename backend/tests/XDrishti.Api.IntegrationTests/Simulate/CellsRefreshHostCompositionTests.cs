using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning.CellsRefresh;
using XDrishti.Application.Platform.Services;
using XDrishti.Hosting;
using XDrishti.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// LC-01: every host composition builds with the container validated (a missing service fails at build, not in the middle of a run), and the refresh handler, which needs the run-record writer only the web host has, exists only there. The service itself is registered
/// everywhere (the gate text and the bell read its state), and a host that does not run services never resolves the handler.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CellsRefreshHostCompositionTests(ApiFactory factory)
{
    private static readonly Dictionary<string, string?> NoDatabase = new()
    {
        ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=1",
        ["BrokerVault:Key"] = string.Empty,
    };

    [Fact]
    public void The_cli_host_builds_validated_and_knows_the_service_but_not_the_handler_that_needs_the_run_record_writer()
    {
        using var host = SimulateHost.Create(NoDatabase);
        using var scope = host.Services.CreateScope();

        host.Services.GetRequiredService<ServiceRegistry>().Find("cells-refresh").ShouldNotBeNull();
        scope.ServiceProvider.GetService<ICommandHandler<RefreshCellStatsCommand, RefreshCellStatsOutcome>>().ShouldBeNull();
    }

    [Fact]
    public void The_worker_composition_builds_with_the_container_validated()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = null, ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddInMemoryCollection(NoDatabase);
        builder.AddXDrishtiDefaults("xd-worker");
        builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        using var host = builder.Build();

        host.Services.GetRequiredService<ServiceRegistry>().Find("cells-refresh").ShouldNotBeNull();
    }

    [Fact]
    public void The_web_host_resolves_the_refresh_handler_where_the_services_run()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshCellStatsCommand, RefreshCellStatsOutcome>>().ShouldNotBeNull();
    }
}
