namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>One row of <c>market.basket_fetches</c> (insert-only). <c>WarningsJson</c> is a JSON array of short texts (matched by symbol, unmatched and retired symbols); no file content.</summary>
public sealed record FetchRecord(
    Guid Id,
    Guid BasketId,
    DateTimeOffset FetchedAt,
    int? HttpStatus,
    string? Etag,
    string? LastModified,
    string? Sha256,
    int Rows,
    string Outcome,
    int Added,
    int Removed,
    int Unmatched,
    string WarningsJson,
    string? Error,
    bool Forced,
    string Actor,
    string? SettingsSnapshot);
