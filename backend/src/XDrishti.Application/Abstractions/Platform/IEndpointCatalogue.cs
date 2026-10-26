using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>The host's route table as the phase check of P8 reads it (spec P8-17): method and pattern of every endpoint. Implemented by the API host over its endpoint data source.</summary>
public interface IEndpointCatalogue
{
    /// <summary>Every route; null when this host has no route table (the command line).</summary>
    IReadOnlyList<ReportsPhaseRoute>? Routes();
}
