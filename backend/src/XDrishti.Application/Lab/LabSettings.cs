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
public sealed record LabSettings(int QuickYears, int QuickSymbols, int MaxParallelRuns, string CostSheetId, TimeSpan PollInterval, QualificationGates Gates, int MinFoldsForPass, bool MultipleTesting)
{
    /// <summary>The method of this slice.</summary>
    public const string QuickMethod = "quick";

    /// <summary>Doc 16: one year, 30 symbols, two runs at once; the sheet called <c>default</c>.</summary>
    public static LabSettings Default { get; } = new(1, 30, 2, "default", TimeSpan.FromSeconds(1), QualificationGates.Default, 6, true);

    /// <summary>The text that stands for these settings in the key of a run (D3): only what changes a result.</summary>
    public string ConfigText(string costBasis) =>
        "quick.years=" + QuickYears.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ";quick.symbols=" + QuickSymbols.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ";fills=modelled;costs=" + costBasis;
}
