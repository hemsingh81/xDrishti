namespace XDrishti.Domain.Market.Quality;

/// <summary>One instrument's result in one run (<c>market.quality_results</c>, keyed by run and instrument): status, score, the bars and missing minutes of the window and the checks.</summary>
public sealed class QualityResult
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private QualityResult(Guid runId, Guid instrumentId, string status, int score, int barsChecked, int missingMinutes, QualityChecks checks)
    {
        RunId = runId;
        InstrumentId = instrumentId;
        Status = status;
        Score = score;
        BarsChecked = barsChecked;
        MissingMinutes = missingMinutes;
        Checks = checks;
    }

    public Guid RunId { get; private set; }

    public Guid InstrumentId { get; private set; }

    /// <summary><see cref="CheckStatus.Pass"/>, <see cref="CheckStatus.Warning"/> or <see cref="CheckStatus.Fail"/>.</summary>
    public string Status { get; private set; }

    public int Score { get; private set; }

    public int BarsChecked { get; private set; }

    public int MissingMinutes { get; private set; }

    public QualityChecks Checks { get; private set; }

    public static QualityResult From(Guid runId, Guid instrumentId, QualityOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new QualityResult(runId, instrumentId, outcome.Status, outcome.Score, outcome.BarsChecked, outcome.MissingMinutes, outcome.Checks);
    }
}
