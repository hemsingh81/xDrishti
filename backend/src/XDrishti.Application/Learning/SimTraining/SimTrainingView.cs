using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning.SimTraining;

/// <summary>Trains on a completed simulate-everything job (spec AU-13). Runs after the job ends; the same inputs give the same record and a second run writes nothing.</summary>
public sealed record TrainFromSimulationCommand(Guid JobId) : ICommand;

/// <summary>Reads the training of a job (spec AU-13). Read only.</summary>
public sealed record GetSimTrainingQuery(Guid JobId) : IQuery<Result<SimTrainingView>>;

/// <summary>The out-of-fold score of the model beside the best baseline (log loss and Brier score: lower is better).</summary>
public sealed record SimTrainingScore(double CandidateLogLoss, double BaselineLogLoss, string BaselineName, double CandidateBrier, double BaselineBrier);

/// <summary>The summary card of a training from the simulation (spec AU-13, acceptance 8).</summary>
public sealed record SimTrainingView(
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
    bool HoldOutRead,
    SimTrainingScore? Score,
    string? Verdict,
    long? ModelId,
    string Role,
    bool AlreadyBuilt,
    string Summary,
    DateTimeOffset CreatedAt)
{
    /// <summary>The state of a training that produced a challenger.</summary>
    public const string Trained = "trained";

    /// <summary>The state of a training that lacked something and says what.</summary>
    public const string NotEvaluableState = "not_evaluable";

    internal static SimTrainingView Of(SimTrainingRecord r, bool already) => new(
        r.JobId, r.State, r.NotEvaluable, r.Runs, r.RunsNotFinished, r.Rows, r.SampleEvery, r.FoldsUsed, r.FoldsTotal, r.EmbargoSessions, r.EmbargoDays, r.WindowFrom, r.WindowTo, r.HoldOutStart, false,
        r.CandidateLogLoss is { } c && r.BaselineLogLoss is { } b && r.CandidateBrier is { } cb && r.BaselineBrier is { } bb && r.BaselineName is { } n ? new SimTrainingScore(c, b, n, cb, bb) : null,
        r.Verdict, r.ModelId, r.ModelId is null ? "none" : "challenger", already, r.Summary, r.CreatedAt);
}
