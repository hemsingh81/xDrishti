using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Capabilities;

/// <summary>Reads the capabilities (spec P8-18 Part A). Reads only; this handler has no write port.</summary>
internal sealed class GetCapabilitiesHandler(CapabilityViews views) : IQueryHandler<GetCapabilitiesQuery, CapabilitiesView>
{
    public async ValueTask<CapabilitiesView> HandleAsync(GetCapabilitiesQuery query, CancellationToken cancellationToken) =>
        await views.BuildAsync(cancellationToken);
}
