namespace XDrishti.Domain.Learning;

/// <summary>One retrain window of the counterfactual (P6-12): both sides scored the same rows.</summary>
/// <param name="Index">1-based among the windows of the period.</param>
/// <param name="Start">First day.</param>
/// <param name="End">Last day.</param>
/// <param name="Rows">Rows scored by each side.</param>
/// <param name="Days">Distinct trade days among them.</param>
/// <param name="Instruments">Distinct instruments among them.</param>
/// <param name="FrozenTrainRows">Rows the frozen model was fitted on (the same in every window).</param>
/// <param name="RetrainedTrainRows">Rows the retrained model was fitted on for this window.</param>
/// <param name="Informative">The retrained model saw labels the frozen one did not; only these windows are judged.</param>
/// <param name="Pooled">Both sides on the window's rows.</param>
public sealed record CounterfactualWindow(int Index, DateOnly Start, DateOnly End, int Rows, int Days, int Instruments, int FrozenTrainRows, int RetrainedTrainRows, bool Informative, CounterfactualPooled Pooled);
