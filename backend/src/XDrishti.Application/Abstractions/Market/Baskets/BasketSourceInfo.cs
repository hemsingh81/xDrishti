namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>A catalogue row (<c>market.basket_sources</c>) with the basket's name and the number of members it has now. <c>Format</c> is <c>format_version</c>: an index list, a list read from the master (never fetched), or one of the two NSE F&amp;O files (BK-05).</summary>
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
    int CurrentMembers,
    string Format = "nse-index-csv-1");
