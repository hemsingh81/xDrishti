using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Readiness;
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
        services.AddSingleton<HaltAutoClearService>();

        // The readiness record and probe (P12-11, ADR 0164): it reads the stored connection and, through IBrokerFacts, the broker's profile and configured IP, and writes insert-only rows. IBrokerFacts is the closed default (unreadable, so the verdict is unavailable) unless the
        // sandbox composition registered the Dhan adapter of those two reads first (sandbox and Execution:SendEnabled, ADR 0170) or live readiness is on (below, ADR 0171). Nothing here can send an order.
        services.AddSingleton<PgReadinessStore>();
        services.AddSingleton<IReadinessStore>(sp => sp.GetRequiredService<PgReadinessStore>());
        services.AddSingleton<IReadinessAccounts>(sp => sp.GetRequiredService<PgReadinessStore>());
        services.AddSingleton<IConnectionFacts>(sp => sp.GetRequiredService<PgReadinessStore>());
        // ADR 0171 step 6 and ADR 0172: in live, and only with Execution:ReadinessLiveEnabled on (default OFF), the two reads take the account's token from the sealed hand-over (SELECT only, never the vault) and go out only through the host-pinned proxy
        // xd-exec-proxy (HTTPS_PROXY, set only by deploy/compose.exec-live.yaml); without it they answer no_live_route. The live guard rows are the two GETs only. Not an IExecCredentials: the order client never sees this token and still refuses live.
        if (ExecEnvironmentParser.Parse(configuration[$"{ExecOptions.Section}:Environment"]) == ExecEnvironment.Live && ExecOptions.IsReadinessLiveEnabled(configuration))
        {
            services.AddOptions<ExecHandoverKeyOptions>().Bind(configuration.GetSection(ExecHandoverKeyOptions.Section));
            services.AddSingleton<ILiveTokenSource, PgExecTokenHandover>();
            var proxy = ExecOptions.LiveProxy(configuration);
            services.TryAddSingleton<IBrokerFacts>(sp => new LiveBrokerFacts(
                sp.GetRequiredService<ILiveTokenSource>(), enabled: true, LiveBrokerFacts.ProxySocket(proxy), sp.GetRequiredService<ILogger<LiveBrokerFacts>>(), sp.GetRequiredService<TimeProvider>()));
        }

        services.TryAddSingleton<IBrokerFacts, ClosedBrokerFacts>();
        services.AddSingleton<IReadinessSettings>(sp => sp.GetRequiredService<ExecSettingsCache>());
        services.AddSingleton<ReadinessSignal>();
        services.AddSingleton<ReadinessLoop>();
        services.AddHostedService<ReadinessService>();

        // The reconciliation record, our side of the books (read only) and the authoritative fill writer (P12-06, ADR 0162). Nothing here runs: the break service, the recovery, the poll and the stream client are composed by no host.
        services.AddSingleton<IReconStore, PgReconStore>();
        services.AddSingleton<IReconOurSource, PgReconOurSource>();
        services.AddSingleton<IFillWriter, PgFillWriter>();
        services.AddSingleton<PgSendJournal>();
        services.AddSingleton<ISendJournal>(sp => sp.GetRequiredService<PgSendJournal>());
        services.AddSingleton<ISendHistory>(sp => sp.GetRequiredService<PgSendJournal>());
        services.AddSingleton<ITokenGate, PgTokenGate>();
        // The confirm record (P12-08b, ADR 0166): only the safe direction runs here. The expiry loop closes unanswered tickets and the closer refuses a ticket the owner's tap was refused for (a command of the API). The issuer, the
        // stored confirm gate and the hand-off are composed by no host: IConfirmGate stays the closed default and no account can be semi_auto (ADR 0163).
        services.AddSingleton<PgConfirmTickets>();
        services.AddSingleton<IConfirmTicketStore>(sp => sp.GetRequiredService<PgConfirmTickets>());
        services.TryAddSingleton<IConfirmGate, ClosedConfirmGate>();
        services.AddSingleton<TicketCloser>();
        services.AddHostedService<TicketExpiryService>();
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
