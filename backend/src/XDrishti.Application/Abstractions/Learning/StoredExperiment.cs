namespace XDrishti.Application.Abstractions.Learning;

/// <summary>An experiment read back from the ledger.</summary>
/// <param name="Id">The ledger id.</param>
/// <param name="Experiment">What was recorded; the report text is empty in a list.</param>
public sealed record StoredExperiment(long Id, ExperimentToStore Experiment);
