namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What an append to the experiment ledger did.</summary>
/// <param name="Id">The ledger id of the experiment.</param>
/// <param name="Created">False when the experiment key was stored already and nothing was written.</param>
public sealed record RecordedExperiment(long Id, bool Created);
