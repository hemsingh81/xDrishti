using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// The placebo walk of the Lab's chance tests as the use case reaches it (spec CF-07 part B, ADR 0209): a port in front of the engine, which is pure calculation and is reached from an adapter in Infrastructure. Like <see cref="IRandomEntryEngine"/> it
/// does no I/O: the use case reads the bars and hands them over in the same <see cref="InstrumentReplayInput"/> a replay takes.
/// </summary>
public interface IPlaceboEngine
{
    /// <summary>
    /// Walks the (signal, shape) pairs of the plan whose placement is on this instrument, costed with the input's sheet. Pure and synchronous; a pair that makes no trade says why. <paramref name="pairDone"/> is told 1 after each pair and a cancelled
    /// <paramref name="cancellationToken"/> stops the walk with an <see cref="OperationCanceledException"/>. A handle that this engine did not make throws <see cref="ArgumentException"/>.
    /// </summary>
    PlaceboWalkResult WalkPlacebos(IPreparedStrategy strategy, InstrumentReplayInput input, PlaceboWalkPlan plan, IProgress<int>? pairDone, CancellationToken cancellationToken);
}
