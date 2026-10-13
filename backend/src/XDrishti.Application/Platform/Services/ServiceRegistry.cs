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

    public IScheduledService? Find(string id) => _byId.GetValueOrDefault(id);

    public string NameOf(string id) => _byId.TryGetValue(id, out var service) ? service.Definition.Name : id;
}
