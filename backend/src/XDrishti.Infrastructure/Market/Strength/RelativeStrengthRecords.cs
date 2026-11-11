namespace XDrishti.Infrastructure.Market.Strength;

/// <summary>A row of <c>market.relative_strength_daily</c> (BK-09): insert-only for the runtime role. <see cref="BasketId"/> is the empty id for the index-relative row; there is no foreign key on it for that reason.</summary>
internal sealed class RelativeStrengthRecord
{
    public DateOnly AsOf { get; set; }

    public Guid InstrumentId { get; set; }

    public Guid BasketId { get; set; }

    public int Lookback { get; set; }

    public int Skip { get; set; }

    public double? Score { get; set; }

    public double? PctRank { get; set; }

    public int NRanked { get; set; }

    public string State { get; set; } = string.Empty;

    public string MembershipBasis { get; set; } = string.Empty;
}

/// <summary>A row of <c>market.sector_strength_daily</c> (BK-09): insert-only for the runtime role.</summary>
internal sealed class SectorStrengthRecord
{
    public DateOnly AsOf { get; set; }

    public string Sector { get; set; } = string.Empty;

    public int WindowSessions { get; set; }

    public double? Ret { get; set; }

    public int? Rank { get; set; }

    public int NMembers { get; set; }

    public string State { get; set; } = string.Empty;
}
