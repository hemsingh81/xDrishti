using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

public static class DhanSandboxRegistration
{
    /// <summary>
    /// Composes the sandbox send path. Called only when the process environment is <c>sandbox</c> (<see cref="Host.ExecServices.AddExecution"/>) and, inside, only when the owner-level host setting <c>Execution:SendEnabled</c> is on (default OFF).
    /// The raw adapter is never a service: <see cref="IBrokerOrders"/> is not registered. What is registered is <see cref="LegSender"/> (the only order surface: gate, halt, token, collar, rate, write-ahead), <see cref="LegLookup"/> (reads one order by our
    /// client id) and a read-only view <see cref="IBrokerReads"/>. No redirects, a 5 s connect timeout, no proxy, no cookies, and the guard in front of the socket. Nothing in the running host calls <see cref="LegSender"/>.
    /// </summary>
    public static IServiceCollection AddDhanSandbox(this IServiceCollection services, IConfiguration configuration)
    {
        if (!Host.ExecOptions.IsSendEnabled(configuration))
        {
            return services;
        }

        services.AddOptions<DhanSandboxOptions>().Bind(configuration.GetSection(DhanSandboxOptions.Section)).ValidateDataAnnotations();
        services.AddSingleton<IExecCredentials, SandboxCredentials>();
        // The checklist step (P12-03, ADR 0159). No facts source is wired yet, so it still refuses every entry (check_facts_unavailable); exits go to the closed exit policy.
        services.AddSingleton<ICheckFactsLoader, ClosedCheckFactsLoader>();
        services.AddSingleton<IExitSendPolicy, ClosedExitSendPolicy>();
        services.AddSingleton<IPreSendCheck>(sp => new ChecklistPreSendCheck(
            sp.GetRequiredService<ICheckFactsLoader>(), sp.GetRequiredService<ICheckStore>(), sp.GetRequiredService<IExitSendPolicy>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp =>
        {
            var socket = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5), AutomaticDecompression = System.Net.DecompressionMethods.None };
            var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, socket)) { Timeout = Timeout.InfiniteTimeSpan };
            var settings = sp.GetRequiredService<ISendSettings>();
            return new SandboxBroker(new DhanOrderClient(
                http, sp.GetRequiredService<IExecCredentials>(), ExecEnvironment.Sandbox, sp.GetRequiredService<ILogger<DhanOrderClient>>(), sp.GetRequiredService<TimeProvider>(),
                () => settings.Current()?.SendTimeout ?? TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
        });
        services.AddSingleton<IBrokerReads>(sp => new ReadOnlyBroker(sp.GetRequiredService<SandboxBroker>().Client));
        services.AddSingleton(sp => new LegLookup(sp.GetRequiredService<SandboxBroker>().Client, sp.GetRequiredService<TimeProvider>()));
        return services.AddLegSender();
    }

    /// <summary>The send protocol. Needs the persistent journal, token gate and send history of the execution record (<c>AddExecutionRecord</c>); without them it is not buildable and nothing can send. The caps and the validity window come from the settings in force.</summary>
    private static IServiceCollection AddLegSender(this IServiceCollection services) =>
        services.AddSingleton(sp => new LegSender(
            sp.GetRequiredService<SandboxBroker>().Client,
            sp.GetRequiredService<IModeSource>(),
            sp.GetRequiredService<IHaltSource>(),
            sp.GetRequiredService<IPreSendCheck>(),
            sp.GetRequiredService<ISendJournal>(),
            new ExecRateLimiter(sp.GetRequiredService<TimeProvider>(), 2, 30, 200),
            ExecEnvironment.Sandbox,
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(5),
            sp.GetRequiredService<ISendSettings>(),
            sp.GetRequiredService<ITokenGate>(),
            sp.GetRequiredService<ISendHistory>()));
}

/// <summary>Holds the one adapter instance for the composition above. Internal and not an <see cref="IBrokerOrders"/>: no service in the container exposes the raw order calls.</summary>
internal sealed class SandboxBroker(DhanOrderClient client)
{
    public DhanOrderClient Client { get; } = client;
}

/// <summary>Only the GET calls of the adapter, so that a consumer of <see cref="IBrokerReads"/> cannot reach the write methods by a cast.</summary>
internal sealed class ReadOnlyBroker(IBrokerReads inner) : IBrokerReads
{
    public Task<BrokerRead<IReadOnlyList<BrokerOrderView>>> GetOrderBookAsync(CancellationToken cancellationToken) => inner.GetOrderBookAsync(cancellationToken);

    public Task<BrokerRead<IReadOnlyList<BrokerTradeView>>> GetTradeBookAsync(CancellationToken cancellationToken) => inner.GetTradeBookAsync(cancellationToken);

    public Task<BrokerRead<IReadOnlyList<BrokerPositionView>>> GetPositionsAsync(CancellationToken cancellationToken) => inner.GetPositionsAsync(cancellationToken);

    public Task<BrokerRead<BrokerFundsView>> GetFundsAsync(CancellationToken cancellationToken) => inner.GetFundsAsync(cancellationToken);
}
