using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:PairedBootstrap</c> settings of CF-10 part 10c-1 (ADR 0216): the paired day-cluster bootstrap that gives the probability that one scorer beats another (spec D4). Every number is a proposal (MinDays and the tie tolerance are this slice's own, ADR 0216), not a constant.
/// The figure is shown in the validation report beside the ladder and nothing decides on it (the predicate that will is part 10c-2), so there is no enforce switch and <see cref="ConfidenceMode.Gate"/> is refused. The pass bar (0.95) is <see cref="LadderOptions.MinProbability"/>.
/// <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (nothing computed, the ladder reads the probability as not available) or <see cref="ConfidenceMode.ReportOnly"/> (the default).</param>
/// <param name="Draws">Resamples of the trade days (default 2000; 100 to 20000).</param>
/// <param name="Level">The coverage of the interval of the gain (default 0.90).</param>
/// <param name="MinDays">The fewest trade days for a figure (default 10; 2 to 1000): on fewer days the resamples repeat the same few values and the probability is only 0 or 1, so it is not available.</param>
/// <param name="PromisingFrom">A probability from this (default 0.90) and below the pass bar reads "promising", never pass.</param>
public sealed record PairedBootstrapOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    int Draws = 2000,
    double Level = 0.90,
    int MinDays = 10,
    double PromisingFrom = 0.90)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values, for a stored document that must say which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"{Mode}/draws={Draws}/level={Level:R}/minDays={MinDays}/promising={PromisingFrom:R}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode) || Mode == ConfidenceMode.Gate)
        {
            problems.Add("PairedBootstrap:Mode must be Off or ReportOnly (nothing here enforces)");
        }

        if (Draws is < 100 or > 20000)
        {
            problems.Add("PairedBootstrap:Draws must be 100 to 20000");
        }

        if (!double.IsFinite(Level) || !(Level > 0.5 && Level < 1))
        {
            problems.Add("PairedBootstrap:Level must be above 0.5 and below 1");
        }

        if (MinDays is < 2 or > 1000)
        {
            problems.Add("PairedBootstrap:MinDays must be 2 to 1000");
        }

        if (!double.IsFinite(PromisingFrom) || PromisingFrom is < 0.5 or > 0.99)
        {
            problems.Add("PairedBootstrap:PromisingFrom must be 0.50 to 0.99");
        }

        return problems.AsReadOnly();
    }
}
