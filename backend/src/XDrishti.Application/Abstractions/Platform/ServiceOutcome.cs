using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>How one attempt of a service ended: succeeded, failed (retried when retries are left) or skipped (nothing was done, with the reason; never retried). The message is short and safe to show.</summary>
public sealed record ServiceOutcome(ServiceRunStatus Status, string? Message = null)
{
    public static ServiceOutcome Succeeded(string? message = null) => new(ServiceRunStatus.Succeeded, message);

    public static ServiceOutcome Failed(string message) => new(ServiceRunStatus.Failed, message);

    public static ServiceOutcome Skipped(string reason) => new(ServiceRunStatus.Skipped, reason);
}
