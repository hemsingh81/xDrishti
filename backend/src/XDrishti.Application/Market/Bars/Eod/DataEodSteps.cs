using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// The steps of the scheduled end-of-day run: every step of the pipeline (<see cref="DataSteps.All"/>) except the CSV load (the CSV folder is for history the owner brings by hand), so
/// <c>dhan</c>, <c>aggregate</c> and then the data quality checks (P1-14a), in that order. This is the one place that decides it. "Update data" by hand runs <see cref="DataSteps.Default"/>, which has no quality step.
/// </summary>
public static class DataEodSteps
{
    public static IReadOnlyList<string> Planned() => [.. DataSteps.All.Where(step => step != DataSteps.Csv)];
}
