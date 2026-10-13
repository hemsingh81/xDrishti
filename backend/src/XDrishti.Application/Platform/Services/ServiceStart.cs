using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>What asking a service to run came to: the run that started, or that the service was running already (a skipped run was recorded) or does not exist.</summary>
public sealed record ServiceStart(ServiceRun? Run, bool AlreadyRunning, bool Unknown)
{
    public static ServiceStart Started(ServiceRun run) => new(run, false, false);

    public static ServiceStart Busy(ServiceRun? skipped) => new(skipped, true, false);

    public static ServiceStart NoSuchService { get; } = new(null, false, true);
}
