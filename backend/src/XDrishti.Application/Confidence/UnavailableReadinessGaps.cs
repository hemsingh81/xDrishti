using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The one implementation of <see cref="IReadinessGaps"/>: every input that has no producer yet says <c>unavailable</c> and why (spec CF-11, ADR 0221). When a producer is built it replaces this answer behind the port; nothing in the calculators changes. The reasons are shown beside
/// the rows and listed in the card's "waiting for" list.
/// </summary>
internal sealed class UnavailableReadinessGaps : IReadinessGaps
{
    private static ValueTask<ReadinessReading<T>> None<T>(string reason)
        where T : struct => ValueTask.FromResult(new ReadinessReading<T>(null, "unavailable: " + reason));

    public ValueTask<ReadinessReading<double>> RiskShareAsync(string strategy, CancellationToken cancellationToken) =>
        None<double>("the plan does not expose a planned risk share per strategy; the system level uses equal weights and says so.");
}
