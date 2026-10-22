using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-04b2 criterion 2: the host of the verb. It builds with the container validated, carries no web part, needs no vault key, starts nothing, logs to stderr and takes no command-line arguments. No database is reached.</summary>
public sealed class SimulateHostTests
{
    private static readonly Dictionary<string, string?> NoDatabase = new()
    {
        ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=1",
        ["BrokerVault:Key"] = string.Empty,
    };

    [Fact]
    public void The_host_builds_with_the_container_validated_and_resolves_the_handlers_the_verb_uses()
    {
        using var host = SimulateHost.Create(NoDatabase);
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>().ShouldNotBeNull();
        services.GetRequiredService<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>>().ShouldNotBeNull();
        services.GetRequiredService<IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>>>().ShouldNotBeNull();
        services.GetRequiredService<IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>>>().ShouldNotBeNull();
        services.GetRequiredService<IQueryHandler<GetBasketMembersQuery, Result<BasketMembers>>>().ShouldNotBeNull();
        host.Services.GetRequiredService<ISimulationEngine>().BuiltIns.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_scope_validation_would_fail_the_build_so_the_provider_is_built_with_both_validations()
    {
        // a scoped service resolved from the root provider throws only when ValidateScopes is on
        using var host = SimulateHost.Create(NoDatabase);

        Should.Throw<InvalidOperationException>(() => host.Services.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>());
    }

    [Fact]
    public void A_service_that_cannot_be_constructed_fails_at_build_not_in_the_middle_of_a_run()
    {
        Should.Throw<Exception>(() => SimulateHost.Create(NoDatabase, services => services.AddScoped<MissingDependencyConsumer>()));
    }

    [Fact]
    public void An_empty_vault_key_does_not_fail_the_start_validation_because_only_the_api_host_requires_it()
    {
        using var host = SimulateHost.Create(NoDatabase);

        host.Services.GetRequiredService<IConfiguration>()["BrokerVault:Key"].ShouldBeNullOrEmpty();
        Should.NotThrow(() => host.Services.GetService<IStartupValidator>()?.Validate());
    }

    [Fact]
    public void The_host_is_built_and_not_started_so_no_hosted_service_runs()
    {
        using var host = SimulateHost.Create(NoDatabase);

        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeFalse();
        host.Services.GetServices<IHostedService>().Select(h => h.GetType().FullName!).ShouldNotContain(n => n.StartsWith("XDrishti.", StringComparison.Ordinal), "no hosted service of ours is registered (only the framework's health-check publisher is)");
    }

    [Fact]
    public void The_console_sink_is_told_to_write_everything_to_stderr_and_the_override_merges_into_the_console_element()
    {
        using var host = SimulateHost.Create(NoDatabase);
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        configuration["Serilog:WriteTo:0:Args:standardErrorFromLevel"].ShouldBe("Verbose");
        configuration["Serilog:WriteTo:0:Name"].ShouldBe("Console", "appsettings.json was found at the content root and element 0 of WriteTo is the console sink");
        configuration["Database:ConnectionString"].ShouldBe(NoDatabase["Database:ConnectionString"], "the overrides win");
    }

    [Fact]
    public void The_content_root_is_where_appsettings_was_copied_so_the_default_connection_string_is_found_from_any_working_directory()
    {
        using var host = SimulateHost.Create();

        host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath.TrimEnd(Path.DirectorySeparatorChar).ShouldBe(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        host.Services.GetRequiredService<IConfiguration>()["Database:ConnectionString"].ShouldNotBeNull().ShouldContain("Port=5433");
    }

    [Fact]
    public void The_arguments_of_the_process_never_become_configuration()
    {
        using var host = SimulateHost.Create(NoDatabase);
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        configuration["urls"].ShouldBeNull();
        configuration["environment"].ShouldBeNull();
        configuration["strategy"].ShouldBeNull();
        Environment.GetCommandLineArgs().Where(a => a.StartsWith("--urls", StringComparison.Ordinal)).ShouldBeEmpty("the host builds from no arguments whatever the test process was given");
    }

    [Fact]
    public void The_configure_hook_replaces_services_after_the_registrations()
    {
        var engine = new MarkerEngine();

        using var host = SimulateHost.Create(NoDatabase, services => services.AddSingleton<ISimulationEngine>(engine));

        host.Services.GetRequiredService<ISimulationEngine>().ShouldBeSameAs(engine);
    }

    internal sealed class MissingDependencyConsumer(IMissingDependency missing)
    {
        public IMissingDependency Missing { get; } = missing;
    }

    internal interface IMissingDependency;

    private sealed class MarkerEngine : ISimulationEngine
    {
        public string EngineVersion => "marker";

        public IReadOnlyList<BuiltInStrategyInfo> BuiltIns => [];

        public Result<IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, XDrishti.Domain.Market.Bars.Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null) => throw new NotSupportedException();

        public ComparisonReplay ReplayComparison(IPreparedStrategy strategy, InstrumentReplayInput input) => throw new NotSupportedException();

        public InstrumentReplay Replay(IPreparedStrategy strategy, InstrumentReplayInput input) => throw new NotSupportedException();
    }
}
