namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// The quality phase of a data job (P1-14a): <c>Done</c> of <c>Total</c> are instruments checked. <c>RunId</c> is the quality run it recorded
/// (<c>/api/data/quality/runs</c>); <c>Passed</c>, <c>Warned</c> and <c>Failed</c> count the instruments by result so far. Part of the API (the job's
/// <c>quality</c> block, present only when the job asked for the step).
/// </summary>
public sealed record QualityPhase(
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long Done,
    long Total,
    string? Message,
    string? Code,
    Guid? RunId,
    long Passed,
    long Warned,
    long Failed) : IDataPhase
{
    public static QualityPhase Empty(string status) => new(status, null, null, 0, 0, null, null, null, 0, 0, 0);
}
