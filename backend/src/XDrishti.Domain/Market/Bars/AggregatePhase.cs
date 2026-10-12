namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// The aggregate phase of a data job (ADR 0016 §6): <c>Done</c> of <c>Total</c> are instruments. <c>Buckets</c> counts the bars written (new or changed),
/// by timeframe; <c>Full</c> is true when everything was rebuilt rather than the ranges touched by this job. Part of the API (the job's <c>aggregate</c> block).
/// </summary>
public sealed record AggregatePhase(
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long Done,
    long Total,
    string? Message,
    string? Code,
    bool Full,
    long Buckets,
    IReadOnlyDictionary<string, long> BucketsByTimeframe) : IDataPhase
{
    public static AggregatePhase Empty(string status) => new(status, null, null, 0, 0, null, null, false, 0, new Dictionary<string, long>());
}
