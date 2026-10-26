using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Reporting;

namespace XDrishti.Api.Platform;

/// <summary>The route table of this host for the P8 phase check (spec P8-17): the real routes and their methods from the endpoint data source, so a write added later under a guarded prefix is seen even if a text search would miss it.</summary>
internal sealed class EndpointCatalogue(EndpointDataSource source) : IEndpointCatalogue
{
    public IReadOnlyList<ReportsPhaseRoute>? Routes() =>
    [
        .. source.Endpoints.OfType<RouteEndpoint>().SelectMany(e =>
        {
            var pattern = "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            return methods is { Count: > 0 } ? methods.Select(m => new ReportsPhaseRoute(m.ToUpperInvariant(), pattern)) : [new ReportsPhaseRoute("ANY", pattern)];
        }),
    ];
}
