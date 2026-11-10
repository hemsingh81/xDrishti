namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>The new state of a source after a check (everything but the url and bounds, which only the catalogue sets). <c>KnownThrough</c> (BK-05, the ban list): the newest trade date of a file that was applied or confirmed; null leaves the stored date as it is, so a failure never moves it.</summary>
public sealed record SourceUpdate(
    Guid BasketId,
    bool Enabled,
    string State,
    DateTimeOffset LastCheckedAt,
    DateTimeOffset? LastOkAt,
    string? Etag,
    string? LastModified,
    string? Sha256,
    DateTimeOffset? NextAttemptAt,
    int FailCount,
    DateOnly? KnownThrough = null);
