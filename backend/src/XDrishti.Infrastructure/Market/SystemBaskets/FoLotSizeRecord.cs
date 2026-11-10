namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>A row of <c>market.fo_lot_sizes</c> (insert-only): the F&amp;O lot size NSE's file gave for an instrument on a date. The master's own lot size is never overwritten by it.</summary>
internal sealed class FoLotSizeRecord
{
    public Guid InstrumentId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public int LotSize { get; set; }

    public string ExpiryLabel { get; set; } = string.Empty;

    public Guid FetchId { get; set; }
}
