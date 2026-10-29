using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

public static class DhanSandboxRegistration
{
    /// <summary>
    /// Composes the sandbox adapter and the send protocol. Called only when the process environment is <c>sandbox</c> (<see cref="Host.ExecServices.AddExecution"/>); the live environment registers nothing. No redirects, a 5 s connect timeout,
    /// no proxy, no cookies, and the guard in front of the socket. Nothing in the running host calls <see cref="LegSender"/> yet.
    /// </summary>
    public static IServiceCollection AddDhanSandbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DhanSandboxOptions>().Bind(configuration.GetSection(DhanSandboxOptions.Section)).ValidateDataAnnotations();
        services.AddSingleton<IExecCredentials, SandboxCredentials>();
        services.AddSingleton<IPreSendCheck, ClosedPreSendCheck>();
        services.AddSingleton<IBrokerOrders>(sp =>
        {
            var socket = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5), AutomaticDecompression = System.Net.DecompressionMethods.None };
            var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, socket)) { Timeout = Timeout.InfiniteTimeSpan };
            return new DhanOrderClient(http, sp.GetRequiredService<IExecCredentials>(), ExecEnvironment.Sandbox, sp.GetRequiredService<ILogger<DhanOrderClient>>(), sp.GetRequiredService<TimeProvider>(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        });
        return services;
    }

    /// <summary>The send protocol; a separate call so that a host that wants the adapter without the protocol can say so. Needs a registered <see cref="ISendJournal"/> (P12-02b); without one it is not buildable and nothing can send.</summary>
    public static IServiceCollection AddLegSender(this IServiceCollection services) =>
        services.AddSingleton(sp => new LegSender(
            sp.GetRequiredService<IBrokerOrders>(),
            sp.GetRequiredService<IModeSource>(),
            sp.GetRequiredService<IHaltSource>(),
            sp.GetRequiredService<IPreSendCheck>(),
            sp.GetRequiredService<ISendJournal>(),
            new ExecRateLimiter(sp.GetRequiredService<TimeProvider>(), 2, 30, 200),
            ExecEnvironment.Sandbox,
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(5)));
}
