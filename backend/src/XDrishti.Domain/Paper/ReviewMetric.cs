using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Paper;

/// <summary>One figure of a review section on the wire and in the stored record: a state and, only for value and provisional, a number (P8-00 D3).</summary>
/// <param name="Id">The figure's name.</param>
/// <param name="Label">The words.</param>
/// <param name="State">value, provisional, insufficient, unavailable, not_evaluable or empty.</param>
/// <param name="Number">The number; null unless value or provisional.</param>
/// <param name="Reason">Why it is not a plain value.</param>
/// <param name="N">How many items it rests on, when that is meaningful.</param>
public sealed record ReviewMetric(string Id, string Label, string State, double? Number, string? Reason, int? N)
{
    /// <summary>The figure of a <see cref="Metric"/>.</summary>
    public static ReviewMetric Of(string id, string label, Metric metric, int? n = null)
    {
        ArgumentNullException.ThrowIfNull(metric);
        return new ReviewMetric(id, label, Name(metric.State), metric.Number, metric.Reason, n);
    }

    /// <summary>The snake-case name of a state.</summary>
    public static string Name(MetricState state) => state switch
    {
        MetricState.Value => "value",
        MetricState.Provisional => "provisional",
        MetricState.Insufficient => "insufficient",
        MetricState.Unavailable => "unavailable",
        MetricState.NotEvaluable => "not_evaluable",
        _ => "empty",
    };
}
