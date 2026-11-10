namespace XDrishti.Application.Market.Baskets;

/// <summary>The state of a system basket's file: <c>state</c> never · ok · stale · rejected · failed; <c>unmatched</c> = members of the last applied or checked list that are not in the instrument list ("N members not in your instrument list").</summary>
public sealed record BasketSourceSummary(string State, DateTimeOffset? LastOkAt, DateTimeOffset? LastCheckedAt, int Unmatched);

/// <summary>One fetch: <c>outcome</c> unchanged · applied · rejected · failed · skipped; <c>error</c> is the short reason of a rejection (tripwire, too_many_changes, row_count, bad_header, unmatched, not_csv, http_404 ...) or failure. No file content.</summary>
public sealed record FetchOutcomeView(
    Guid BasketId, DateTimeOffset At, string Outcome, int? HttpStatus, int Rows, int Added, int Removed, int Unmatched, string? Error, bool Forced);

/// <summary>The source of a system basket and its last 20 fetches (no raw file). <c>enabled</c> = the owner has turned this file on.</summary>
public sealed record BasketSourceView(
    Guid BasketId, string Name, string FileName, string SourceUrl, bool Enabled, string State, DateTimeOffset? LastOkAt, DateTimeOffset? LastCheckedAt,
    DateTimeOffset? NextAttemptAt, int ExpectedMin, int ExpectedMax, IReadOnlyList<FetchOutcomeView> Fetches);

/// <summary>Fetch now: the given baskets (the sources are turned on by this owner act) or, without ids, every enabled source; <c>force</c> overrides only the tripwire. An owner act: needs a step-up.</summary>
public sealed record FetchNowInput(IReadOnlyList<Guid>? BasketIds, bool? Force);

/// <summary>What a fetch run did, one outcome per source it checked.</summary>
public sealed record BasketFetchRun(IReadOnlyList<FetchOutcomeView> Outcomes)
{
    public string Summary =>
        Outcomes.Count == 0 ? "no enabled source" : string.Join(", ", Outcomes.GroupBy(o => o.Outcome).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}"));
}
