using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Domain.Common;

namespace XDrishti.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers. Explicit registration: no reflection scanning, fast startup, obvious wiring.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>, GetSystemStatusHandler>();
        services.AddScoped<ICommandHandler<RecordHeartbeatCommand>, RecordHeartbeatHandler>();

        services.AddScoped<ICommandHandler<LoginCommand, LoginResult>, LoginHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutHandler>();
        services.AddScoped<IQueryHandler<VerifySessionQuery, Result<VerifiedSession>>, VerifySessionHandler>();
        services.AddScoped<IQueryHandler<GetAuditQuery, IReadOnlyList<AuditEventResponse>>, GetAuditHandler>();
        services.AddScoped<ICommandHandler<ProvisionOwnerCommand, ProvisionOutcome>, ProvisionOwnerHandler>();
        services.AddScoped<ICommandHandler<ResetOwnerPasswordCommand>, ResetOwnerPasswordHandler>();

        return services;
    }
}
