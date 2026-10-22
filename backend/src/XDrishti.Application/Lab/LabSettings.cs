using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// The <c>strategy_lab</c> keys the runs read (doc 16, spec P5-02; ADR 0058): the quick test's years and symbols, how many runs go at once, and the id of the charge sheet a quick test is costed with when one is stored. Registered by the host from its
/// configuration (section <c>StrategyLab</c>); <see cref="Default"/> is doc 16.
/// </summary>
/// <param name="QuickYears">Years of stored data a quick test replays, ending at the last stored session.</param>
/// <param name="QuickSymbols">Most instruments in a quick test.</param>
/// <param name="MaxParallelRuns">Runs that go at once; the rest stay queued.</param>
/// <param name="CostSheetId">The stored charge sheet a run is costed with; a run is gross, and says so, when it is not stored.</param>
/// <param name="PollInterval">How often the dispatcher looks for a queued run.</param>
/// <param name="Gates">The qualification gates a verdict is judged against (doc 16, P5-03).</param>
/// <param name="MinFoldsForPass">Fewer walk-forward folds than this cap a verdict at MARGINAL.</param>
/// <param name="MultipleTesting">Whether the gates tighten as the variants of a family grow.</param>
/// <param name="FullExits">The <c>--exit</c> flags of the candidate exits of a full test (P5-04); null is <see cref="DefaultFullExits"/>.</param>
public sealed record LabSettings(int QuickYears, int QuickSymbols, int MaxParallelRuns, string CostSheetId, TimeSpan PollInterval, QualificationGates Gates, int MinFoldsForPass, bool MultipleTesting, IReadOnlyList<string>? FullExits = null)
{
    /// <summary>The method of this slice.</summary>
    public const string QuickMethod = "quick";

    /// <summary>The method of the full test (P5-04).</summary>
    public const string FullMethod = "full";

    /// <summary>The candidate exits of a full test when none are configured: a fixed reward-to-risk grid, 2R first (the default exit).</summary>
    public static readonly IReadOnlyList<string> DefaultFullExits = ["FIXED_RR:rr=2|1.5|2.5|3"];

    /// <summary>The flags in force.</summary>
    public IReadOnlyList<string> Exits => FullExits is { Count: > 0 } ? FullExits : DefaultFullExits;

    /// <summary>Doc 16: one year, 30 symbols, two runs at once; the sheet called <c>default</c>.</summary>
    public static LabSettings Default { get; } = new(1, 30, 2, "default", TimeSpan.FromSeconds(1), QualificationGates.Default, 6, true);

    /// <summary>The text that stands for these settings in the key of a run (D3): only what changes a result.</summary>
    public string ConfigText(string costBasis) =>
        "quick.years=" + QuickYears.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ";quick.symbols=" + QuickSymbols.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ";fills=modelled;costs=" + costBasis;

    /// <summary>The text that stands for these settings in the key of a full test (P5-04): the exits and the selection options.</summary>
    public string FullConfigText(string costBasis) => FullConfigText(costBasis, new XDrishti.Domain.Research.SelectionOptions());

    private string FullConfigText(string costBasis, XDrishti.Domain.Research.SelectionOptions o) =>
        "full.exits=" + string.Join(" ", Exits) + ";select=expanding," + string.Join(",", new[] { o.MinTrainMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), o.TestMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), o.EmbargoSessions.ToString(System.Globalization.CultureInfo.InvariantCulture), o.Lambda.ToString(System.Globalization.CultureInfo.InvariantCulture), o.MinTrades.ToString(System.Globalization.CultureInfo.InvariantCulture) }) + (o.PreferPlateau ? ",plateau" : string.Empty) + ";fills=modelled;costs=" + costBasis;
}
