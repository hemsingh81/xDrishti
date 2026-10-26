using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Platform.Services;

/// <summary>The services of the application (ADR 0018 §1): every <see cref="IScheduledService"/> that a module registered, by id. The rules only look at these, so the checks of a service that does not exist yet do not apply.</summary>
public sealed class ServiceRegistry
{
    private readonly Dictionary<string, IScheduledService> _byId;

    public ServiceRegistry(IEnumerable<IScheduledService> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        All = [.. services];
        _byId = [];
        foreach (var service in All)
        {
            if (!_byId.TryAdd(service.Definition.Id, service))
            {
                throw new InvalidOperationException($"Two services are registered with the id '{service.Definition.Id}'.");
            }
        }
    }

    /// <summary>In the order they were registered (the order of the screen).</summary>
    public IReadOnlyList<IScheduledService> All { get; }

    /// <summary>The ids of every registered service.</summary>
    public IReadOnlyCollection<string> Ids => _byId.Keys;

    /// <summary>The ids of the services that another process runs (<see cref="IExternallyRunService"/>): shown and edited here, never started, retried or closed by this process.</summary>
    public IReadOnlyCollection<string> ExternalIds => [.. All.Where(s => s is IExternallyRunService).Select(s => s.Definition.Id)];

    public IScheduledService? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Whether the service is registered and run by another process.</summary>
    public bool IsExternal(string id) => _byId.GetValueOrDefault(id) is IExternallyRunService;

    /// <summary>True for the one external service that has the "Run now" marker and the heartbeat of <c>xd-feed</c> (the live feed). The depth recorder (P7-16) is run by the same host but shares neither: pausing, resuming or "Run now" of it never touches the live feed's manual run.</summary>
    public bool UsesLiveFeedMarker(string id) => _byId.GetValueOrDefault(id) is XDrishti.Application.Live.LiveFeedService;

    public string NameOf(string id) => _byId.TryGetValue(id, out var service) ? service.Definition.Name : id;
}
