using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// The labels that say what a P3 number means (spec P3-04b1, E5; the roadmap section "What a P3 number means"): printed first on the console, written into <c>run.json</c> and <c>README.txt</c>, shown above every trade card. A label is
/// built from the run record and the fill rules it ran with, and the rules must be the ones the run recorded, so that a label cannot say a thing the run did not do. The text names no figure about performance.
/// </summary>
public static class RunLabels
{
    /// <summary>The label that the engine is not yet verified against a hand recomputation (P3-08). P3-08 changes it, with its ADR.</summary>
    public const string EngineVerification = "engine: not yet verified (P3-08)";

    /// <summary>Whether the engine has been verified; <c>run.json</c> writes it as <c>engine_verified</c>. False while <see cref="EngineVerification"/> says "not yet verified"; P3-08 changes both together.</summary>
    public const bool EngineVerified = false;

    /// <summary>The first label: a run is an exploration.</summary>
    public const string Exploration = "exploration: signal-level mechanics on past data, not evidence of an edge";

    /// <summary>The fills label when the slippage is none: <see cref="FillBasis.Exact"/> means no slippage and no tick rounding of a fill, and "fills modelled" is never dropped.</summary>
    public const string FillsWithoutSlippage = "fills: modelled, no slippage, no tick rounding of fills";

    private const string Fills = "fills: modelled";

    private const string SurvivorshipNote = "; survivorship is bounded and disclosed, not avoided (ISS-22)";

    private const string TickLabel = "tick: today's master tick; rounding to the grid is approximate";

    private const string OpeningBarLabel = "opening bar: the stored 09:15 bar can be widened by the CSV importer and the first 1-minute open is not the auction open (ISS-21, ISS-18)";

    private const double DaysPerYear = 365.25;

    /// <summary>The three labels of the engine's own gross result, in order: costs, portfolio limits, sample (the engine's header, <c>SimulationHeader.FixedLabels</c>, delegates here). A run record takes the first from <see cref="CostDescription.Label"/>.</summary>
    public static IReadOnlyList<string> FixedLabels { get; } = Array.AsReadOnly([CostDescription.None, "portfolio limits: not simulated", "sample: in-sample"]);

    /// <summary>
    /// The labels of a run, in this order: exploration; fills; entry limit; costs (<see cref="CostDescription.Label"/> of the run's <see cref="SimulationRun.Costs"/>: <c>costs: none</c> for a gross run); the sizing line (a costed run only);
    /// portfolio limits; sample; universe; data; engine verification; tick; opening bar. The not-modelled list is <see cref="FillLimits.NotModelled"/> as it is for a gross run, followed by <see cref="CostLimits.NotModelled"/> for a costed one.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="rules"/> are not the rules the run recorded (<see cref="SimulationRun.Fills"/>).</exception>
    public static RunLabelSet Of(SimulationRun run, FillRules rules)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rules);
        if (!string.Equals(rules.ToCanonicalJson(), run.Fills, StringComparison.Ordinal))
        {
            throw new ArgumentException("The fill rules are not the ones the run recorded; a label must say what the run did.", nameof(rules));
        }

        var entryLimit = FillDescription.Labels(rules)[1];
        string[] lines =
        [
            Exploration,
            rules.Slippage.IsNone ? FillsWithoutSlippage : Fills,
            entryLimit,
            CostDescription.Label(run.Costs),
            .. (run.Costs is null ? Array.Empty<string>() : [CostDescription.SizingLabel(run.Costs)]),
            FixedLabels[1],
            FixedLabels[2],
            "universe: " + run.Universe.Label + SurvivorshipNote,
            DataLine(run),
            EngineVerification,
            TickLabel,
            OpeningBarLabel,
        ];

        return new RunLabelSet(Array.AsReadOnly(lines), run.Costs is null ? FillLimits.NotModelled : Array.AsReadOnly([.. FillLimits.NotModelled, .. CostLimits.NotModelled]));
    }

    /// <summary>The data line from the instruments that ran: how many, the range of their first and of their last bars, and the shortest and longest length in years; <c>data: none</c> when none ran.</summary>
    private static string DataLine(SimulationRun run)
    {
        var ran = run.Instruments.Where(i => i.Skipped is null && i.FirstBar is not null && i.LastBar is not null).ToList();
        if (ran.Count == 0)
        {
            return "data: none";
        }

        var firstMin = ran.Min(i => i.FirstBar!.Value);
        var firstMax = ran.Max(i => i.FirstBar!.Value);
        var lastMin = ran.Min(i => i.LastBar!.Value);
        var lastMax = ran.Max(i => i.LastBar!.Value);
        var lengths = ran.Select(i => (i.LastBar!.Value.DayNumber - i.FirstBar!.Value.DayNumber) / DaysPerYear).ToList();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"data: {ran.Count} instruments; first bars {Date(firstMin)} to {Date(firstMax)}, last bars {Date(lastMin)} to {Date(lastMax)}; length from about {Years(lengths.Min())} to about {Years(lengths.Max())} years (days / 365.25, one decimal); no hold-out (ISS-19)");
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Years(double years) => Math.Round(years, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);
}
