using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>What asking a service to run came to: the run that started, or that the service was running already (a skipped run was recorded), does not exist or is run by another process (<see cref="RunsElsewhere"/>).</summary>
public sealed record ServiceStart(ServiceRun? Run, bool AlreadyRunning, bool Unknown, bool RunsElsewhere = false)
{
    public static ServiceStart Started(ServiceRun run) => new(run, false, false);

    public static ServiceStart Busy(ServiceRun? skipped) => new(skipped, true, false);

    public static ServiceStart NoSuchService { get; } = new(null, false, true);

    /// <summary>The service is run by another process (<see cref="XDrishti.Application.Abstractions.Platform.IExternallyRunService"/>): nothing starts here.</summary>
    public static ServiceStart NotRunHere { get; } = new(null, false, false, true);
}
