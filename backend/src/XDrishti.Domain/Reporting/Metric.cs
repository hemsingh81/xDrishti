namespace XDrishti.Domain.Reporting;

/// <summary>
/// One reported figure: a state and, only for <see cref="MetricState.Value"/> and <see cref="MetricState.Provisional"/>, a finite number. A non-value has no number at all (null, never 0), and a non-finite result is turned into
/// <see cref="MetricState.NotEvaluable"/>, so no consumer can read a digit out of a state that has none. Built through the factories only.
/// </summary>
public sealed record Metric
{
    private Metric(MetricState state, double? number, string? reason)
    {
        State = state;
        Number = number;
        Reason = reason;
    }

    /// <summary>The state.</summary>
    public MetricState State { get; }

    /// <summary>The number; null unless <see cref="State"/> is <see cref="MetricState.Value"/> or <see cref="MetricState.Provisional"/>.</summary>
    public double? Number { get; }

    /// <summary>Why the figure is not a plain value; null for <see cref="MetricState.Value"/>.</summary>
    public string? Reason { get; }

    /// <summary>A number (finite; else not evaluable).</summary>
    public static Metric Of(double number) =>
        double.IsFinite(number) ? new Metric(MetricState.Value, number, null) : NotEvaluable("the result is not a finite number");

    /// <summary>A number that rests on a provisional input.</summary>
    public static Metric OfProvisional(double number, string reason) =>
        double.IsFinite(number) ? new Metric(MetricState.Provisional, number, reason) : NotEvaluable("the result is not a finite number");

    /// <summary>Trades exist but too few.</summary>
    public static Metric Insufficient(string reason) => new(MetricState.Insufficient, null, reason);

    /// <summary>An input does not exist.</summary>
    public static Metric Unavailable(string reason) => new(MetricState.Unavailable, null, reason);

    /// <summary>The rule cannot be applied to what exists.</summary>
    public static Metric NotEvaluable(string reason) => new(MetricState.NotEvaluable, null, reason);

    /// <summary>No trade.</summary>
    public static Metric Empty(string reason) => new(MetricState.Empty, null, reason);

    /// <summary>The same figure marked provisional; a figure that is not a plain value is returned as it is.</summary>
    public Metric AsProvisional(string reason) => State == MetricState.Value ? new Metric(MetricState.Provisional, Number, reason) : this;
}
