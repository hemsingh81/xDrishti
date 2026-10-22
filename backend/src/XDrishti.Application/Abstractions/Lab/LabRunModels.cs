using XDrishti.Domain.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>One instrument of a Lab run's universe.</summary>
public sealed record LabInstrument(Guid Id, string Symbol);

/// <summary>
/// The universe of a Lab run as it was resolved when the run was posted (spec P5-02, D2): the basket changes later, the run keeps the list. <paramref name="Requested"/> is the number of members before the quick-test sample;
/// <paramref name="Sampled"/> says the list was cut to the limit by the seeded shuffle of <paramref name="Seed"/>.
/// </summary>
public sealed record LabUniverse(string Kind, Guid? BasketId, string Label, int Requested, IReadOnlyList<LabInstrument> Instruments, bool Sampled, long? Seed);

/// <summary>The counts of a finished run: the instruments asked for and replayed, the ones skipped, and the trades (closed and open at the end of the data).</summary>
public sealed record LabCounts(int Instruments, int InstrumentsRun, int Skipped, int Trades, int OpenAtEnd);

/// <summary>One located problem of a run, with the stage that found it.</summary>
public sealed record LabProblem(string Stage, string Path, string Code, string Message, string Severity, int? Line, int? Column);

/// <summary>One entry of the stage log: when a stage began and ended and how many problems it found.</summary>
public sealed record LabStageLog(string Name, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int Problems);

/// <summary>A row of <c>lab.lab_runs</c> (spec P5-02, ADR 0058).</summary>
public sealed record LabRun(
    Guid Id,
    string Key,
    string StrategyId,
    int Version,
    string StrategyHash,
    LabUniverse Universe,
    DateOnly From,
    DateOnly To,
    string Method,
    string ConfigHash,
    string EngineVersion,
    LabRunState State,
    string? Stage,
    int Done,
    int Total,
    IReadOnlyList<string> SimRunKeys,
    LabCounts? Counts,
    IReadOnlyList<LabProblem> Problems,
    IReadOnlyList<LabStageLog> Stages,
    IReadOnlyList<string> Labels,
    string? Error,
    bool CancelRequested,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>What a post inserts: everything that identifies the run and the labels it carries from the start.</summary>
public sealed record NewLabRun(
    Guid Id,
    string Key,
    string StrategyId,
    int Version,
    string StrategyHash,
    LabUniverse Universe,
    DateOnly From,
    DateOnly To,
    string Method,
    string ConfigHash,
    string EngineVersion,
    IReadOnlyList<string> Labels,
    DateTimeOffset CreatedAt);

/// <summary>The run a post ended with and whether it was made now (false: the run of the same key that already held it).</summary>
public sealed record InsertedLabRun(LabRun Run, bool Created);

/// <summary>How a run ended, for <see cref="ILabRunStore.FinishAsync"/>.</summary>
public sealed record LabFinish(
    Guid Id,
    LabRunState State,
    string? Stage,
    IReadOnlyList<string> SimRunKeys,
    LabCounts? Counts,
    IReadOnlyList<LabProblem> Problems,
    IReadOnlyList<LabStageLog> Stages,
    string? Error,
    DateTimeOffset FinishedAt);

/// <summary>What a cancel did.</summary>
public enum LabCancelOutcome
{
    /// <summary>No such run.</summary>
    NotFound,

    /// <summary>A queued run was cancelled at once.</summary>
    Cancelled,

    /// <summary>A running run was asked to stop at its next safe point.</summary>
    Requested,

    /// <summary>The run has finished.</summary>
    NotCancellable,
}
