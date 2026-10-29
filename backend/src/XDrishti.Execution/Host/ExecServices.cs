using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Host;

public static class ExecServices
{
    /// <summary>Registers the skeleton with closed defaults. No HTTP client, no broker adapter: the only <see cref="IBrokerOrders"/> is not registered at all in this slice.</summary>
    public static IServiceCollection AddExecution(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExecOptions>().Bind(configuration.GetSection(ExecOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IModeSource, ClosedModeSource>();
        services.AddSingleton<IHaltSource, ClosedHaltSource>();
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
