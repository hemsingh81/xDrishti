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

    public ValueTask<ReadinessReading<bool>> BenchmarkBeatenAsync(string strategy, int? version, CancellationToken cancellationToken) =>
        None<bool>("no same-universe benchmark producer exists (P8-07 baskets benchmarks give no risk-adjusted net comparison per strategy yet).");

    public ValueTask<ReadinessReading<bool>> TwentyTradeCheckPassedAsync(string strategy, CancellationToken cancellationToken) =>
        None<bool>("the P3-09 20-trade check is the owner's signed record and has no machine-readable store.");

    public ValueTask<ReadinessReading<double>> QuarterShareAsync(string strategy, int? version, CancellationToken cancellationToken) =>
        None<double>("the Lab verdict has a by-year table but no by-quarter table.");

    public ValueTask<ReadinessReading<double>> MaxInstrumentShareAsync(string strategy, int? version, CancellationToken cancellationToken) =>
        None<double>("the Lab verdict has no per-instrument share of the total R.");

    public ValueTask<ReadinessReading<double>> McDrawdownP90Async(string strategy, int? version, CancellationToken cancellationToken) =>
        None<double>("the Lab verdict stores the 50th and the 95th percentile of the Monte Carlo drawdown, not the 90th.");

    public ValueTask<ReadinessReading<bool>> IntegrityFaultAsync(CancellationToken cancellationToken) =>
        None<bool>("the list of P7 and P10 integrity checks is unverified, so no source reads them.");

    public ValueTask<ReadinessReading<bool>> TierCOnlyAsync(string strategy, CancellationToken cancellationToken) =>
        None<bool>("whether \"evidence only from tier C\" means the Lab trades or the forward trades is not decided (owner question, ADR 0223), so it is not known whether the evidence is only from tier C.");

    public ValueTask<ReadinessReading<double>> RiskShareAsync(string strategy, CancellationToken cancellationToken) =>
        None<double>("the plan does not expose a planned risk share per strategy; the system level uses equal weights and says so.");
}
