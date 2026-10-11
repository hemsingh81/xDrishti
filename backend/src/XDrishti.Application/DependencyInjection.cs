using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Accounts.RiskProfiles;
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
        services.AddScoped<ICommandHandler<StepUpCommand, StepUpResult>, StepUpHandler>();
        services.AddScoped<IQueryHandler<VerifySessionQuery, Result<VerifiedSession>>, VerifySessionHandler>();
        services.AddScoped<IQueryHandler<GetAuditQuery, IReadOnlyList<AuditEventResponse>>, GetAuditHandler>();
        services.AddScoped<ICommandHandler<ProvisionOwnerCommand, ProvisionOutcome>, ProvisionOwnerHandler>();
        services.AddScoped<ICommandHandler<ResetOwnerPasswordCommand>, ResetOwnerPasswordHandler>();

        services.AddAccounts();

        return services;
    }

    private static void AddAccounts(this IServiceCollection services)
    {
        services.AddScoped<AccountViews>();

        services.AddScoped<IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountSummary>>, ListAccountsHandler>();
        services.AddScoped<IQueryHandler<GetAccountQuery, Result<AccountDetail>>, GetAccountHandler>();
        services.AddScoped<IQueryHandler<GetAccountActivityQuery, Result<IReadOnlyList<AccountEventView>>>, GetAccountActivityHandler>();
        services.AddScoped<ICommandHandler<CreateAccountCommand, AccountDetail>, CreateAccountHandler>();
        services.AddScoped<ICommandHandler<UpdateConnectionCommand, AccountDetail>, UpdateConnectionHandler>();
        services.AddScoped<ICommandHandler<UpdateSecretsCommand, AccountDetail>, UpdateSecretsHandler>();
        services.AddScoped<ICommandHandler<UpdateRolesCommand, AccountDetail>, UpdateRolesHandler>();
        services.AddScoped<ICommandHandler<MakePrimaryDataCommand, AccountDetail>, MakePrimaryDataHandler>();
        services.AddScoped<ICommandHandler<UpdateTradingCommand, AccountDetail>, UpdateTradingHandler>();
        services.AddScoped<ICommandHandler<UpdatePortfolioCommand, AccountDetail>, UpdatePortfolioHandler>();
        services.AddScoped<ICommandHandler<UpdateDataCommand, AccountDetail>, UpdateDataHandler>();
        services.AddScoped<ICommandHandler<ArchiveAccountCommand>, ArchiveAccountHandler>();

        services.AddScoped<IQueryHandler<GetDhanRedirectQuery, DhanRedirectView>, GetDhanRedirectHandler>();

        services.AddScoped<ConnectionWriter>();
        services.AddScoped<ICommandHandler<StartConnectCommand, ConnectStartView>, StartConnectHandler>();
        services.AddScoped<ICommandHandler<CompleteConnectCommand, ConnectReturn>, CompleteConnectHandler>();
        services.AddScoped<ICommandHandler<DisconnectCommand, AccountDetail>, DisconnectHandler>();

        services.AddScoped<IQueryHandler<ListRiskProfilesQuery, IReadOnlyList<RiskProfileView>>, ListRiskProfilesHandler>();
        services.AddScoped<ICommandHandler<CreateRiskProfileCommand, RiskProfileView>, CreateRiskProfileHandler>();
        services.AddScoped<ICommandHandler<UpdateRiskProfileCommand, RiskProfileView>, UpdateRiskProfileHandler>();
        services.AddScoped<ICommandHandler<DeleteRiskProfileCommand>, DeleteRiskProfileHandler>();

        services.AddScoped<IQueryHandler<ListBucketsQuery, IReadOnlyList<BucketView>>, ListBucketsHandler>();
        services.AddScoped<ICommandHandler<AddBucketCommand, BucketView>, AddBucketHandler>();
    }
}
