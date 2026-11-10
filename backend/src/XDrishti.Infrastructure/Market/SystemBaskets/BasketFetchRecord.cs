namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>A row of <c>market.basket_fetches</c>: one check of a source, insert-only for the runtime role. No file content is kept, only counts, the hash and short reasons.</summary>
internal sealed class BasketFetchRecord
{
    public Guid Id { get; set; }

    public Guid BasketId { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public int? HttpStatus { get; set; }

    public string? Etag { get; set; }

    public string? LastModified { get; set; }

    public string? Sha256 { get; set; }

    public int Rows { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public int Added { get; set; }

    public int Removed { get; set; }

    public int Unmatched { get; set; }

    public string Warnings { get; set; } = "[]";

    public string? Error { get; set; }

    public bool Forced { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string? SettingsSnapshot { get; set; }
}
