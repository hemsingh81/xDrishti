using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;

namespace XDrishti.Api.Auth;

/// <summary>
/// Deployment steps that put the owner credentials (from secret files) into the database:
/// <c>migrate</c> provisions the owner when none exists, <c>owner-reset</c> replaces the password and signs out everywhere.
/// Returns the process exit code. The password is never logged.
/// </summary>
internal static partial class OwnerBootstrap
{
    public static async Task<int> ProvisionAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = CreateLogger(scope.ServiceProvider);
        var secrets = ReadSecrets(scope.ServiceProvider);

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ProvisionOwnerCommand, ProvisionOutcome>>();
        var result = await handler.HandleAsync(new ProvisionOwnerCommand(secrets.Email, secrets.Name, secrets.Password), cancellationToken);
        if (result.IsFailure)
        {
            LogMissingSecrets(logger, result.Error.Code);
            return 1;
        }

        return 0;
    }

    public static async Task<int> ResetAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = CreateLogger(scope.ServiceProvider);
        var secrets = ReadSecrets(scope.ServiceProvider);

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ResetOwnerPasswordCommand>>();
        var result = await handler.HandleAsync(new ResetOwnerPasswordCommand(secrets.Email, secrets.Name, secrets.Password), cancellationToken);
        if (result.IsFailure)
        {
            LogResetFailed(logger, result.Error.Code);
            return 1;
        }

        return 0;
    }

    private static OwnerSecretsOptions ReadSecrets(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>().GetSection(OwnerSecretsOptions.SectionName).Get<OwnerSecretsOptions>() ?? new OwnerSecretsOptions();

    private static ILogger CreateLogger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OwnerBootstrap).FullName!);

    [LoggerMessage(Level = LogLevel.Error, Message = "Owner provisioning failed ({Code}). Check the Auth__Owner__* secrets (see scripts/auth-secrets.sh)")]
    private static partial void LogMissingSecrets(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Owner reset failed ({Code}). Check the Auth__Owner__* secrets (see scripts/auth-secrets.sh)")]
    private static partial void LogResetFailed(ILogger logger, string code);
}
