namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>A row of <c>market.basket_sources</c>: where a system basket's membership comes from and the state of the last check. The catalogue (url, bounds) is data seeded by the migration, disabled.</summary>
internal sealed class BasketSourceRecord
{
    public Guid BasketId { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string? HostAlt { get; set; }

    public string FormatVersion { get; set; } = "nse-index-csv-1";

    public string Cadence { get; set; } = "daily";

    public int ExpectedMin { get; set; }

    public int ExpectedMax { get; set; }

    public bool Enabled { get; set; }

    public string? LicenseNote { get; set; }

    public string State { get; set; } = "never";

    public DateTimeOffset? LastOkAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public string? Etag { get; set; }

    public string? LastModified { get; set; }

    public string? Sha256 { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public int FailCount { get; set; }

    /// <summary>The ban list's newest applied or confirmed trade date (BK-05): the flags it gives are known through this date, an empty list included. Null for the other sources.</summary>
    public DateOnly? KnownThrough { get; set; }
}
