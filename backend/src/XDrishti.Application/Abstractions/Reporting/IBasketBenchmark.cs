using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>The index of the basket profile in force: <see cref="Index"/> is null when it cannot be found, and <see cref="Reason"/> says why (never a fall-back to another index). <see cref="Name"/> is what the owner reads for it.</summary>
public sealed record BasketBenchmark(BenchmarkInstrument? Index, bool IsDefault, string Name, string? Reason);

/// <summary>The basket benchmark setting (BK-06c): the chosen master-list index, else Nifty 500. Separate from the portfolio benchmark.</summary>
public interface IBasketBenchmark
{
    /// <summary>The index in force. A stored choice that is gone from the master is reported missing, not replaced by the default.</summary>
    ValueTask<BasketBenchmark> GetAsync(CancellationToken cancellationToken);
}
