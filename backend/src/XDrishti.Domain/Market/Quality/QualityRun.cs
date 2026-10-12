using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// One run of the data quality checks (<c>market.quality_runs</c>, ADR 0017 §4): what started it, over how many sessions and instruments, and how many passed, warned and failed. A run over every
/// tracked instrument has no <see cref="InstrumentId"/>; the fix actions of the Data quality screen check one instrument again and record a run of their own. At most one runs at a time.
/// Runs are kept for good (the runtime role has no DELETE).
/// </summary>
public sealed class QualityRun : Entity<Guid>
{
    public const int MaxErrorLength = 1000;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private QualityRun(
        Guid id, QualityTrigger trigger, QualityRunStatus status, DateTimeOffset startedAt, DateTimeOffset? finishedAt, int sessions, int instruments,
        int passed, int warned, int failed, double? score, string? error, Guid? jobId, Guid? instrumentId)
        : base(id)
    {
        Trigger = trigger;
        Status = status;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        Sessions = sessions;
        Instruments = instruments;
        Passed = passed;
        Warned = warned;
        Failed = failed;
        Score = score;
        Error = error;
        JobId = jobId;
        InstrumentId = instrumentId;
    }

    public QualityTrigger Trigger { get; private set; }

    public QualityRunStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The trading sessions the run looked at (the window).</summary>
    public int Sessions { get; private set; }

    /// <summary>How many instruments the run checks (known when it starts).</summary>
    public int Instruments { get; private set; }

    public int Passed { get; private set; }

    public int Warned { get; private set; }

    public int Failed { get; private set; }

    /// <summary>The mean score of the instruments checked, 0–100 (null until the run ends or when it checked none).</summary>
    public double? Score { get; private set; }

    public string? Error { get; private set; }

    /// <summary>The data job whose quality step this run is.</summary>
    public Guid? JobId { get; private set; }

    /// <summary>Set when the run checks one instrument only.</summary>
    public Guid? InstrumentId { get; private set; }

    public bool IsRunning => Status == QualityRunStatus.Running;

    public static QualityRun Start(QualityTrigger trigger, int sessions, int instruments, Guid? jobId, Guid? instrumentId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessions);
        ArgumentOutOfRangeException.ThrowIfNegative(instruments);
        return new QualityRun(Guid.CreateVersion7(now), trigger, QualityRunStatus.Running, now, null, sessions, instruments, 0, 0, 0, null, null, jobId, instrumentId);
    }

    /// <summary>Counts one more instrument checked with the given result status (<see cref="CheckStatus"/>).</summary>
    public void Record(string resultStatus)
    {
        if (!IsRunning)
        {
            return;
        }

        switch (resultStatus)
        {
            case CheckStatus.Pass:
                Passed++;
                break;
            case CheckStatus.Warning:
                Warned++;
                break;
            case CheckStatus.Fail:
                Failed++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resultStatus), resultStatus, "A result is pass, warning or fail.");
        }
    }

    public void Finish(DateTimeOffset now, double? meanScore)
    {
        End(now, QualityRunStatus.Succeeded);
        Score = meanScore is { } s ? Math.Round(s, 1) : null;
    }

    public void Fail(DateTimeOffset now, string error)
    {
        End(now, QualityRunStatus.Failed);
        Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }

    private void End(DateTimeOffset now, QualityRunStatus status)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The quality run has already ended.");
        }

        Status = status;
        FinishedAt = now;
    }
}
