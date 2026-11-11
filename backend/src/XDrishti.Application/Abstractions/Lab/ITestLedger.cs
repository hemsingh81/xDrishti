using XDrishti.Domain.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// One row of the test ledger (spec P5-03): a Lab run that was created for a strategy family. <paramref name="TrialKind"/> is <c>run</c> (a run or a sweep point), <c>replan</c> (a changed frozen plan, CF-06) or <c>system_replay</c> (SR-01); <paramref name="ExtraTrials"/> is the number of
/// extra candidate exits the first full test of the hash walked (CF-06, D3), zero on every other row.
/// </summary>
public sealed record LedgerRow(
    Guid RunId,
    string RunKey,
    string StrategyId,
    int Version,
    string StrategyHash,
    string? ParentHash,
    string Universe,
    DateOnly From,
    DateOnly To,
    string Method,
    DateTimeOffset CreatedAt,
    string TrialKind = "run",
    int ExtraTrials = 0)
{
    /// <summary>The trial kind of a training sweep (AU-11, ADR 0243): listed beside the looks, never counted as one and never a variant of the family.</summary>
    public const string TrainingSweepKind = "training_sweep";

    /// <summary>True for a training sweep row; evidence readers of the list skip these.</summary>
    public bool IsTrainingSweep => string.Equals(TrialKind, TrainingSweepKind, StringComparison.Ordinal);

    /// <summary>The trial kind of a system replay (SR-01, ADR 0272): one row per strategy version the run uses, with the run's input hash as the run key. It is a look at the strategy and is counted as one (the kind is not filtered out), with no extra trials.</summary>
    public const string SystemReplayKind = "system_replay";
}

/// <summary>
/// The per-family count of looks (spec P5-03, ADR 0059): <c>lab.test_ledger</c> behind a port. Insert-only for the runtime role; a row stays whatever happens to its run.
/// </summary>
public interface ITestLedger
{
    /// <summary>Appends the row; a second append of the same run id writes nothing and returns false.</summary>
    ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken);

    /// <summary>The distinct strategy hashes recorded for the family: its variant count.</summary>
    ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken);

    /// <summary>The parts of the honest number of looks of the family (CF-06, D3), counted in one query.</summary>
    ValueTask<TrialCounts> CountTrialsAsync(string strategyId, CancellationToken cancellationToken);

    /// <summary>Whether the family already has a full or robust run row with this strategy hash (the extra exits of a hash are counted once).</summary>
    ValueTask<bool> HasFullRunAsync(string strategyId, string strategyHash, CancellationToken cancellationToken);

    /// <summary>Whether the family has a row with this strategy hash (an existence query: nothing is listed).</summary>
    ValueTask<bool> HasHashAsync(string strategyId, string strategyHash, CancellationToken cancellationToken);

    /// <summary>The rows of the family, newest first.</summary>
    ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken);
}
