using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Application.Platform.SystemStatus;

namespace XDrishti.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers. Explicit registration: no reflection scanning, fast startup, obvious wiring.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>, GetSystemStatusHandler>();
        services.AddScoped<ICommandHandler<RecordHeartbeatCommand>, RecordHeartbeatHandler>();

        return services;
    }
}
