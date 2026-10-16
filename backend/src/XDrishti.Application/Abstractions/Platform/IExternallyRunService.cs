namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// Marks a registered <see cref="IScheduledService"/> that another process runs: it is registered here only so that the Services screen can show it and edit its schedule (the live feed, run by <c>xd-feed</c>, whose run records that process
/// owns). The scheduler and the runner of this process never start it, never retry it and never close its runs (the open run is the other process's and survives a restart of this one); its status comes from the heartbeat of the process
/// that runs it. A marker only - it does not extend <see cref="IScheduledService"/>, so a service is still exactly one sealed class.
/// </summary>
public interface IExternallyRunService
{
}
