using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Pbo</c> settings of CF-03 part B (ADR 0228): the probability of backtest overfitting of the Lab's sweep. Defaults are the spec's; <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// Raising <see cref="MinVariants"/> or <see cref="MaxPbo"/> loosens a <see cref="ConfidenceMode.Gate"/>, lowering them tightens it, and <see cref="Blocks"/> and <see cref="Metric"/> move the figure either way: none of the four is claimed to be tighten-only.
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (no figure, no series), <see cref="ConfidenceMode.ReportOnly"/> (the default: figure shown, no outcome changes) or <see cref="ConfidenceMode.Gate"/> (config, tighten-only, it can only cap a PASS at MARGINAL: a PASS is capped at MARGINAL when PBO is above <paramref name="MaxPbo"/>; nothing else changes).</param>
/// <param name="Blocks">S, the contiguous blocks of the window (4..16, even); all <c>C(S, S/2)</c> splits are tried.</param>
/// <param name="MinVariants">The fewest variants PBO is computed on (6..100); fewer reads <c>unavailable</c>.</param>
/// <param name="MaxPbo">The limit of the Gate (0.1..0.9, default 0.5, the paper's "more likely overfit than not").</param>
/// <param name="Metric">The score that picks and ranks the best variant.</param>
public sealed record PboOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    int Blocks = 8,
    int MinVariants = 10,
    double MaxPbo = 0.5,
    PboMetric Metric = PboMetric.Mean)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>True when a PBO above the limit caps a PASS (config, tighten-only).</summary>
    public bool IsGate => Mode == ConfidenceMode.Gate;

    /// <summary>The text of the values, kept with the figure so a stored document says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"{Mode}/{Blocks}/{MinVariants}/{MaxPbo:R}/{Metric}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode))
        {
            problems.Add("Pbo:Mode must be Off, ReportOnly or Gate");
        }

        if (Blocks is < Cscv.MinBlocks or > Cscv.MaxBlocks || Blocks % 2 != 0)
        {
            problems.Add("Pbo:Blocks must be an even number from 4 to 16");
        }

        if (MinVariants is < 6 or > 100)
        {
            problems.Add("Pbo:MinVariants must be 6 to 100");
        }

        if (!double.IsFinite(MaxPbo) || MaxPbo is < 0.1 or > 0.9)
        {
            problems.Add("Pbo:MaxPbo must be 0.1 to 0.9");
        }

        if (!Enum.IsDefined(Metric))
        {
            problems.Add("Pbo:Metric must be Mean or Sharpe");
        }

        return problems.AsReadOnly();
    }
}
