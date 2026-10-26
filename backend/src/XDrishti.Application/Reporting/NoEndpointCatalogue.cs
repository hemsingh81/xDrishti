using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The catalogue of a host without a route table: it says so, and the phase check then reports the read-only criterion NOT EVALUABLE.</summary>
internal sealed class NoEndpointCatalogue : IEndpointCatalogue
{
    public IReadOnlyList<ReportsPhaseRoute>? Routes() => null;
}
