namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// A master-list instrument as the benchmark needs it: <see cref="Symbol"/> is the canonical symbol, <see cref="Name"/> the display name, <see cref="Type"/> the instrument type code (<c>index</c> for a valid benchmark),
/// <see cref="Tracked"/> whether tracking is on, <see cref="HasBars"/> whether any 1-minute bars are stored for it.
/// </summary>
public sealed record BenchmarkInstrument(Guid Id, string Symbol, string Name, string Type, bool Retired, bool Tracked, bool HasBars);

/// <summary>The benchmark in force: the instrument and whether it is the default (the owner has not chosen one).</summary>
public sealed record ConfiguredBenchmark(BenchmarkInstrument Instrument, bool IsDefault);

/// <summary>The benchmark setting of the portfolio (ADR 0020 §2) and the master-list reads it needs.</summary>
public interface IPortfolioBenchmark
{
    /// <summary>The benchmark in force: the stored choice, or the default (Nifty 50) while none is stored or the stored one is gone from the master. Null when the master list has no such index at all.</summary>
    ValueTask<ConfiguredBenchmark?> GetAsync(CancellationToken cancellationToken);

    /// <summary>The master-list instrument with this id (any type, retired included); null when it does not exist.</summary>
    ValueTask<BenchmarkInstrument?> FindAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>Collects the change of the stored choice (and nothing else): <see cref="IPortfolioUnitOfWork"/> saves it together with the audit event.</summary>
    ValueTask SetAsync(Guid instrumentId, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
