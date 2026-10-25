using System.Text;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The wire spelling of the dimensions (lower case) and the figures (snake case) of a report, and the whitelist that reads them back (P8-00 D4). One place, so the query, the views and the contract agree.</summary>
internal static class ReportNames
{
    private static readonly Dictionary<string, ReportDimension> Dimensions = Enum.GetValues<ReportDimension>().ToDictionary(Name, d => d, StringComparer.Ordinal);

    private static readonly Dictionary<string, ReportMetric> Metrics = Enum.GetValues<ReportMetric>().ToDictionary(Name, m => m, StringComparer.Ordinal);

    /// <summary>The dimension names in declaration order.</summary>
    public static IReadOnlyList<string> DimensionNames { get; } = [.. Enum.GetValues<ReportDimension>().Select(Name)];

    /// <summary>The figure names in declaration order.</summary>
    public static IReadOnlyList<string> MetricNames { get; } = [.. Enum.GetValues<ReportMetric>().Select(Name)];

    public static string Name(ReportDimension dimension) => dimension.ToString().ToLowerInvariant();

    public static string Name(ReportMetric metric)
    {
        var text = new StringBuilder();
        foreach (var c in metric.ToString())
        {
            if (char.IsUpper(c) && text.Length > 0)
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }

    public static string Name(MetricState state) => state switch
    {
        MetricState.Value => "value",
        MetricState.Provisional => "provisional",
        MetricState.Insufficient => "insufficient",
        MetricState.Unavailable => "unavailable",
        MetricState.NotEvaluable => "not_evaluable",
        MetricState.Empty => "empty",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static bool TryParseDimension(string? name, out ReportDimension dimension)
    {
        dimension = default;
        return name is not null && Dimensions.TryGetValue(name, out dimension);
    }

    /// <summary>Reads a figure name; an absent name is the default figure, expectancy in R.</summary>
    public static bool TryParseMetric(string? name, out ReportMetric metric)
    {
        if (string.IsNullOrEmpty(name))
        {
            metric = ReportMetric.ExpectancyR;
            return true;
        }

        metric = default;
        return Metrics.TryGetValue(name, out metric);
    }
}
