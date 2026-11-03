using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>The engine version alone (P15-12), read from the registered <see cref="ISimulationEngine"/>.</summary>
internal sealed record EngineVersionOf(string EngineVersion) : ISimulationEngineVersion;
