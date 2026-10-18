using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// A strategy ready to be replayed, as the use case sees it (spec P3-04a, D6): opaque. It exposes only the record a run keeps of the strategy and the warnings of its compile; the engine behind the port holds the compiled sides
/// and hands the handle back to <see cref="ISimulationEngine.Replay"/>. Application cannot name the types of the simulation library, so the handle is an interface made here and implemented by the adapter in Infrastructure.
/// </summary>
public interface IPreparedStrategy
{
    /// <summary>The strategy as a run records it: id, parameters, canonical JSON and its SHA-256, timeframe and sides.</summary>
    StrategyRecord Record { get; }

    /// <summary>The warnings of the compile (path in <see cref="FieldError.Field"/>, code in <see cref="FieldError.Code"/>); counted in the log, not stored in the run record.</summary>
    IReadOnlyList<FieldError> Warnings { get; }
}
