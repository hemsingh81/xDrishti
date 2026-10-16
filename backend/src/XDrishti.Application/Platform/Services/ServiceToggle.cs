using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>What pausing and resuming have in common: switch the service on or off, keep every other choice, audit it.</summary>
internal static class ServiceToggle
{
    public static async ValueTask<Result<ServiceView>> SetEnabledAsync(
        string serviceId,
        bool enabled,
        string actor,
        ServiceRegistry registry,
        ServiceSettingsReader settings,
        ServiceWriter writer,
        ServiceViewReader reader,
        CancellationToken cancellationToken)
    {
        if (serviceId == ServiceIds.AccountSync)
        {
            return Result.Failure<ServiceView>(ServiceErrors.ReadOnly);
        }

        if (registry.Find(serviceId) is null)
        {
            return Result.Failure<ServiceView>(ServiceErrors.NotFound);
        }

        var service = await settings.ReadAsync(serviceId, cancellationToken);
        if (service.Settings.Enabled != enabled)
        {
            var written = await writer.ChangeAsync(
                service,
                current => current with { Enabled = enabled },
                enabled ? AccountEventType.ServiceResumed : AccountEventType.ServicePaused,
                (_, _) => $"{serviceId}: {(enabled ? "resumed" : "paused")}",
                actor,
                cancellationToken);
            if (written.IsFailure)
            {
                return Result.Failure<ServiceView>(written.Error);
            }
        }
        else if (!enabled && registry.IsExternal(serviceId))
        {
            // paused already, but a "Run now" may keep the service up: pausing ends it
            await writer.EndManualRunAsync(actor, cancellationToken);
        }

        return Result.Success((await reader.ReadAsync(serviceId, cancellationToken))!);
    }
}
