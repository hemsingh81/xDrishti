namespace XDrishti.Infrastructure.Market;

/// <summary>A row of <c>market.instruments</c>. Persistence shape only: the list is written by bulk import (<see cref="InstrumentCatalog"/>) and read by projection.</summary>
internal sealed class InstrumentRecord
{
    public Guid Id { get; set; }

    public string CanonicalSymbol { get; set; } = string.Empty;

    public string Exchange { get; set; } = string.Empty;

    public string ExchangeSegment { get; set; } = string.Empty;

    /// <summary>share · etf · index · future.</summary>
    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Isin { get; set; }

    public string? Series { get; set; }

    public int LotSize { get; set; }

    public decimal? TickSize { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string UnderlyingSymbol { get; set; } = string.Empty;

    public string? Sector { get; set; }

    public string? SectorSource { get; set; }

    public bool FnoEligible { get; set; }

    public string? AsmGsmFlag { get; set; }

    public string? AsmGsmCategory { get; set; }

    public decimal? MtfLeverage { get; set; }

    public long? FreezeQuantity { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RetiredAt { get; set; }
}
