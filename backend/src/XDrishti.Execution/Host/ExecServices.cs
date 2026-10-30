using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
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
        AddSendSettings(services);

        // The send path exists only when the owner set the environment to sandbox AND turned Execution:SendEnabled on (default OFF, checked inside). Live registers nothing; none registers nothing.
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
        services.TryAddSingleton(TimeProvider.System);
        AddSendSettings(services);
        services.AddSingleton<ICheckStore, PgCheckStore>();
        services.AddSingleton<IAlarmStore, PgAlarmStore>(); // the alarm record only; the watchdog, the policy and the services that write alarms are composed by no host in P12-04 (ADR 0160)

        // The halt record and the owner's halt command (P12-05, ADR 0161): both can only make the system stricter. IHaltSource stays the closed default (Unreadable) and the enforcer, the drill and the kill switch are composed by no host.
        services.AddSingleton<IHaltStore, PgHaltStore>();
        services.AddSingleton<HaltService>();
        services.AddSingleton<IDayMarkStore, PgDayMarkStore>();

        // The reconciliation record, our side of the books (read only) and the authoritative fill writer (P12-06, ADR 0162). Nothing here runs: the break service, the recovery, the poll and the stream client are composed by no host.
        services.AddSingleton<IReconStore, PgReconStore>();
        services.AddSingleton<IReconOurSource, PgReconOurSource>();
        services.AddSingleton<IFillWriter, PgFillWriter>();
        services.AddSingleton<PgSendJournal>();
        services.AddSingleton<ISendJournal>(sp => sp.GetRequiredService<PgSendJournal>());
        services.AddSingleton<ISendHistory>(sp => sp.GetRequiredService<PgSendJournal>());
        services.AddSingleton<ITokenGate, PgTokenGate>();
        services.AddSingleton<ExecCounters>();
        services.AddHostedService<ExecHeartbeatService>();
        services.AddHostedService<ExecCommandPoller>();
        return services;
    }

    // The settings in force for the send path: filled by the heartbeat service on every read, null (so every send is refused) until the first good read and after a stale one.
    private static void AddSendSettings(IServiceCollection services)
    {
        services.TryAddSingleton<ExecSettingsCache>();
        services.TryAddSingleton<ISendSettings>(sp => sp.GetRequiredService<ExecSettingsCache>());
    }
}
