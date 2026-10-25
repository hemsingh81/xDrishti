namespace XDrishti.Application.Reporting;

/// <summary>What holding each basket, and the benchmark index, would have done over one window (P8-07).</summary>
/// <param name="Window">The window and where it came from.</param>
/// <param name="MembersAsOf">The India date the members are as of; members as of that date, not as of each trading day (survivorship, ISS-22).</param>
/// <param name="Baskets">One entry per basket asked for, in the order asked.</param>
/// <param name="Index">The benchmark index in force.</param>
/// <param name="NonAdditive">An instrument may be in several baskets, so the baskets cannot be added; true always.</param>
/// <param name="MinMembers">Fewest included instruments for a basket number.</param>
/// <param name="Evidence">The evidence block.</param>
public sealed record ReportBenchmarksView(
    BenchmarkWindowView Window,
    DateOnly MembersAsOf,
    IReadOnlyList<BasketBenchmarkView> Baskets,
    IndexBenchmarkView Index,
    bool NonAdditive,
    int MinMembers,
    BenchmarkEvidenceView Evidence);
