namespace XDrishti.Application.Abstractions.SimAll;

/// <summary>The cell statistics of one cell on the <c>training_sim</c> basis (spec AU-13, acceptance 4).</summary>
public sealed record TrainingSimCell(string CellKey, string Strategy, string Timeframe, string Side, string Window, bool Costed, int N, int Wins, double EffectiveN, double WinRatePosterior, double WinRateLower, double WinRateUpper, double? ExpectancyR, bool Enough);

/// <summary>A training from the simulation as it is stored: its inputs, what came out and the summary the report card shows (spec AU-13, acceptance 8).</summary>
public sealed record SimTrainingRecord(
    string InputHash,
    Guid JobId,
    string State,
    string? NotEvaluable,
    int Runs,
    int RunsNotFinished,
    int Rows,
    int SampleEvery,
    int FoldsUsed,
    int FoldsTotal,
    int EmbargoSessions,
    int EmbargoDays,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    DateOnly HoldOutStart,
    double? CandidateLogLoss,
    double? BaselineLogLoss,
    string? BaselineName,
    double? CandidateBrier,
    double? BaselineBrier,
    string? Verdict,
    long? ModelId,
    string? TrainingSetId,
    string Summary,
    DateTimeOffset CreatedAt);

/// <summary>
/// <c>research.sim_training_runs</c> and <c>research.training_sim_cell_stats</c> behind a port (spec AU-13). Insert-only. The cell statistics live in their own table with a fixed <c>training_sim</c> basis, so no reader of the research cell statistics can ever see them.
/// </summary>
public interface ISimTrainingStore
{
    /// <summary>The newest training of a job, or null.</summary>
    ValueTask<SimTrainingRecord?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>The training with this input hash, or null.</summary>
    ValueTask<SimTrainingRecord?> FindAsync(string inputHash, CancellationToken cancellationToken);

    /// <summary>Writes the record and its cells in one transaction; false (and nothing written) when the input hash is stored already.</summary>
    ValueTask<bool> StoreAsync(SimTrainingRecord record, DateOnly cellsAsOf, IReadOnlyList<TrainingSimCell> cells, CancellationToken cancellationToken);
}
