using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Host;

public static class ExecServices
{
    /// <summary>Registers the skeleton with closed defaults. The broker adapter is registered only for environment sandbox (P12-02); the default registers none.</summary>
    public static IServiceCollection AddExecution(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExecOptions>().Bind(configuration.GetSection(ExecOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IModeSource, ClosedModeSource>();
        services.AddSingleton<IHaltSource, ClosedHaltSource>();
        services.TryAddSingleton(TimeProvider.System);

        // The adapter exists only when the owner set the environment to sandbox. Live registers nothing in P12-02; none registers nothing.
        var environment = ExecEnvironmentParser.Parse(configuration[$"{ExecOptions.Section}:Environment"]);
        if (environment == ExecEnvironment.Sandbox)
        {
            services.AddDhanSandbox(configuration);
        }

        return services;
    }

    /// <summary>The record, the settings reader, the command plane and the heartbeat, all as the database role xd_exec (the host refuses any other role).</summary>
    public static IServiceCollection AddExecutionRecord(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExecDatabaseOptions>().Bind(configuration.GetSection(ExecDatabaseOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(sp => new NpgsqlDataSourceBuilder(sp.GetRequiredService<IOptions<ExecDatabaseOptions>>().Value.Build().ConnectionString).Build());
        services.AddSingleton<IExecEventStore, PgExecEventStore>();
        services.AddSingleton<IExecSettingsSnapshots, PgExecSettingsSnapshots>();
        services.AddSingleton<ExecSettingsReader>();
        services.AddSingleton<IExecCommandStore, PgExecCommandStore>();
        services.AddSingleton<ExecCommandProcessor>();
        services.AddSingleton<IExecHeartbeats, PgExecHeartbeats>();
        services.AddSingleton<ExecCounters>();
        services.AddHostedService<ExecHeartbeatService>();
        services.AddHostedService<ExecCommandPoller>();
        return services;
    }
}
