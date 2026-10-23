namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What an append to the experiment ledger did.</summary>
/// <param name="Id">The ledger id of the experiment.</param>
/// <param name="Created">False when the experiment key was stored already and nothing was written.</param>
/// <param name="RequiredTrials">Set (and nothing written) when another experiment was recorded meanwhile, so the trial count the judgement used is no longer the ledger's: the experiment must be judged again with this many trials.</param>
public sealed record RecordedExperiment(long Id, bool Created, int RequiredTrials = 0);
