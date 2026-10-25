namespace XDrishti.Application.Reporting;

/// <summary>The buy-and-hold of one basket with the instruments left out.</summary>
/// <param name="BasketId">The basket.</param>
/// <param name="BasketName">Its name.</param>
/// <param name="Members">Members as of the stated date.</param>
/// <param name="Included">Members the figure rests on.</param>
/// <param name="BuyAndHold">Mean price return in percent, equal weight, no rebalancing.</param>
/// <param name="Excluded">Members left out, each with its reason.</param>
public sealed record BasketBenchmarkView(Guid BasketId, string BasketName, int Members, int Included, MetricView BuyAndHold, IReadOnlyList<BenchmarkExclusionView> Excluded);
