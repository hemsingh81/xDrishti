using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The benchmark index of the portfolio (ADR 0020 §2): the one the owner chose, or Nifty 50 while none is chosen.</summary>
public sealed record GetBenchmarkQuery : IQuery<BenchmarkView>;

internal sealed class GetBenchmarkHandler(IPortfolioBenchmark benchmarks) : IQueryHandler<GetBenchmarkQuery, BenchmarkView>
{
    public async ValueTask<BenchmarkView> HandleAsync(GetBenchmarkQuery query, CancellationToken cancellationToken) =>
        BenchmarkViews.Of(await benchmarks.GetAsync(cancellationToken));
}

internal static class BenchmarkViews
{
    public static BenchmarkView Of(ConfiguredBenchmark? configured) => configured is not { } c
        ? new BenchmarkView(null, null, null, true, false, false)
        : new BenchmarkView(c.Instrument.Id, c.Instrument.Symbol, c.Instrument.Name, c.IsDefault, c.Instrument.Tracked, c.Instrument.HasBars);
}
