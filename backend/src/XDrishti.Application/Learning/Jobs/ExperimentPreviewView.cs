namespace XDrishti.Application.Learning.Jobs;

/// <summary>The baseline a preview is about.</summary>
public sealed record PreviewBaselineView(string StrategyId, DateOnly From, DateOnly To, int Instruments, bool Costed, string? Exit);

/// <summary>One knob of the form.</summary>
public sealed record PreviewKnobView(string Kind, string Name, double Current, double Min, double Max, string Note);

/// <summary>The measured replay time the next job may be compared with: a value only after a job measured one.</summary>
/// <param name="State"><c>value</c> or <c>not_evaluable</c>.</param>
/// <param name="Ms">Milliseconds, for a value.</param>
/// <param name="Text">The sentence.</param>
public sealed record PreviewEstimateView(string State, long? Ms, string Text);

/// <summary>
/// What starting a replay on a baseline would mean, before the owner clicks (spec P8-18 criterion 12). Read only. Every look is a trial: the number it would be, the improvement it would have to show, the folds the data has and the best it can be called.
/// </summary>
public sealed record ExperimentPreviewView(
    PreviewBaselineView Baseline,
    bool Rebuildable,
    string? NotRebuildableReason,
    IReadOnlyList<PreviewKnobView> Knobs,
    int NextTrial,
    double RequiredImprovementR,
    int FoldsAvailable,
    int MinFoldsForBetter,
    string BestPossibleOutcome,
    string? BestPossibleSentence,
    PreviewEstimateView ReplayEstimate,
    int JobsToday,
    int JobsPerDay,
    Guid? ActiveJobId,
    IReadOnlyList<string> Limits);
