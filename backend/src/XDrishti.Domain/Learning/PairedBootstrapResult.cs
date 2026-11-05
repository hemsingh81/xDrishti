using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The paired day-cluster bootstrap of a gain (CF-10 part 10c-1, ADR 0216, spec D4): how likely it is that a model beats a baseline on the same rows. The gain of a row is the baseline's loss minus the model's, so positive is better.
/// </summary>
/// <param name="Rows">Rows.</param>
/// <param name="Days">Distinct trade days (whole days are resampled).</param>
/// <param name="Draws">Resamples.</param>
/// <param name="Level">The interval's coverage.</param>
/// <param name="MeanGain">The mean gain per row on the rows as they are.</param>
/// <param name="ProbabilityBetter">The share of the resamples with a mean gain above zero (a mean within 1e-12 of zero is a tie and not better).</param>
/// <param name="GainLow">The lower end of the percentile interval of the resampled mean gain.</param>
/// <param name="GainHigh">The upper end.</param>
public sealed record PairedBootstrapResult(int Rows, int Days, int Draws, double Level, double MeanGain, double ProbabilityBetter, double GainLow, double GainHigh)
{
    /// <summary>The reading of the probability against the pass bar and the "promising" level.</summary>
    public PairedReading Reading(double passAt, double promisingFrom) =>
        ProbabilityBetter >= passAt ? PairedReading.Pass : ProbabilityBetter >= promisingFrom ? PairedReading.Promising : PairedReading.Below;

    /// <summary>A fixed plain-text rendering with the reading it was given.</summary>
    public string ToText(PairedReading reading) =>
        string.Create(CultureInfo.InvariantCulture, $"P(better) {ProbabilityBetter:F4} ({reading.ToString().ToLowerInvariant()}), mean gain {MeanGain:F4} per trade, {Level * 100:F0}% interval [{GainLow:F4}, {GainHigh:F4}], {Days} days, {Draws} draws");
}
