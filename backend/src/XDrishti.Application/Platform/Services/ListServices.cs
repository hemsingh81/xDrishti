using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Platform.Services;

/// <summary>Every registered service with its schedule, status, latest run, next start and findings, then the read-only entries (the per-account sync). Cheap: a few indexed reads.</summary>
public sealed record ListServicesQuery : IQuery<ServiceList>;

internal sealed class ListServicesHandler(ServiceViewReader reader) : IQueryHandler<ListServicesQuery, ServiceList>
{
    public async ValueTask<ServiceList> HandleAsync(ListServicesQuery query, CancellationToken cancellationToken) =>
        new(await reader.ReadAllAsync(cancellationToken));
}
