namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>A catalogue row (<c>market.basket_sources</c>) with the basket's name and the number of members it has now.</summary>
public sealed record BasketSourceInfo(
    Guid BasketId,
    string Name,
    string FileName,
    string SourceUrl,
    string? HostAlt,
    int ExpectedMin,
    int ExpectedMax,
    bool Enabled,
    string State,
    DateTimeOffset? LastOkAt,
    DateTimeOffset? LastCheckedAt,
    string? Etag,
    string? LastModified,
    string? Sha256,
    DateTimeOffset? NextAttemptAt,
    int FailCount,
    int CurrentMembers);
