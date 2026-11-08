using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Stands for a second implementation of the source of the inputs that have no producer (CF-11 part 1). It is a type of its own, not nested in <see cref="ReadinessArchitectureTests"/>: the closure scan of <see cref="KillArchitectureTests"/> follows a contract to every class that
/// implements it and a nested type to its declaring type, and the gate is reachable from the automatic path, so a nested sentinel would pull the whole test class (and so the registration) into that closure. This one names nothing but the port.
/// </summary>
internal sealed class SecondReadinessGapSentinel : IReadinessGaps
{
    private static ValueTask<ReadinessReading<double>> One() => ValueTask.FromResult(new ReadinessReading<double>(1, string.Empty));

    public ValueTask<ReadinessReading<double>> RiskShareAsync(string strategy, CancellationToken cancellationToken) => One();
}
