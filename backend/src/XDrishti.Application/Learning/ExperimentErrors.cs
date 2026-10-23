using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the experiments (P6-11).</summary>
public static class ExperimentErrors
{
    /// <summary>No experiment with this id.</summary>
    public const string NotFoundCode = "experiments.not_found";

    /// <summary>The runs differ in more than the knob.</summary>
    public const string AssumptionsDifferCode = "experiments.assumptions_differ";

    /// <summary>The candidate took an entry the baseline did not.</summary>
    public const string EntriesAddedCode = "experiments.entries_added";

    /// <summary>The runs are not costed the same way, or one run mixes the two.</summary>
    public const string BasisDiffersCode = "experiments.basis_differs";

    /// <summary>A run holds the same trade key twice.</summary>
    public const string DuplicateTradeCode = "experiments.duplicate_trade";

    /// <summary>No experiment has this id.</summary>
    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No experiment {id} is recorded.");

    /// <summary>The fill basis or the cost sheet of the two runs differs where only the knob may.</summary>
    public static Error AssumptionsDiffer(string what) =>
        Error.Conflict(AssumptionsDifferCode, $"The two runs differ in {what}; an experiment changes one knob, and a changed cost assumption could win unseen.");

    /// <summary>The candidate has entries the baseline lacks: the runs are not on the same entries.</summary>
    public static Error EntriesAdded(int count) =>
        Error.Conflict(EntriesAddedCode, $"The candidate run has {count} entries that the baseline run does not. A change that adds entries is not comparable here; the candidate may only skip entries.");

    /// <summary>The two runs are not costed the same way.</summary>
    public static Error BasisDiffers() =>
        Error.Conflict(BasisDiffersCode, "The runs mix trades with and without charges, or are costed differently; use runs costed the same way.");

    /// <summary>A trade key repeats in a run.</summary>
    public static Error DuplicateTrade(string runKey) =>
        Error.Conflict(DuplicateTradeCode, $"The run {runKey[..Math.Min(12, runKey.Length)]} holds the same trade key twice.");
}
