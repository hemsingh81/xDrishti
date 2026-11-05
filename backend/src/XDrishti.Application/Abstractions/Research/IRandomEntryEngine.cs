using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// The random-entry walk of the Lab's chance test as the use case reaches it (spec CF-07, ADR 0208): a port in front of the engine, which is pure calculation and is reached from an adapter in Infrastructure. Like
/// <see cref="ISimulationEngine"/> it does no I/O: the use case reads the bars and hands them over in the same <see cref="InstrumentReplayInput"/> a replay takes.
/// </summary>
public interface IRandomEntryEngine
{
    /// <summary>
    /// Restates the real trades of the plan that are on this instrument with random entries inside the strategy's own entry window, in every seed, costed with the input's sheet (<see cref="NullTradeSpec"/>, <see cref="NullWalkPlan"/>). Pure and
    /// synchronous; a trade that cannot be restated says why and has no numbers. <paramref name="tradeDone"/> is told 1 after each trade and a cancelled <paramref name="cancellationToken"/> stops the walk with an
    /// <see cref="OperationCanceledException"/>. A handle that this engine did not make throws <see cref="ArgumentException"/>.
    /// </summary>
    NullWalkResult WalkRandomEntries(IPreparedStrategy strategy, InstrumentReplayInput input, NullWalkPlan plan, IProgress<int>? tradeDone, CancellationToken cancellationToken);
}
