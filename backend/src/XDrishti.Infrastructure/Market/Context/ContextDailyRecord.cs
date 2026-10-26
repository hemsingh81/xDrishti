namespace XDrishti.Infrastructure.Market.Context;

/// <summary>A row of <c>market.context_daily</c> (P3-01d): one India trading day of the regime index (high, low, close) and the volatility index close, derived from the stored 1-minute bars. Insert-only for the runtime role, except the six breadth columns (column-level UPDATE).</summary>
internal sealed class ContextDailyRecord
{
    public DateOnly Date { get; set; }

    public Guid IndexInstrumentId { get; set; }

    public double IndexHigh { get; set; }

    public double IndexLow { get; set; }

    public double IndexClose { get; set; }

    public double? VixClose { get; set; }

    public short IndexBars { get; set; }

    public short? VixBars { get; set; }

    /// <summary>Percent (0 to 100) of the owner's tracked shares above their 20-day average; null until computed or when insufficient (P3-01e). The only columns of the row the runtime role may update.</summary>
    public double? Breadth { get; set; }

    public short? BreadthVoters { get; set; }

    public short? BreadthUniverse { get; set; }

    public short? BreadthExcluded { get; set; }

    public string? BreadthState { get; set; }

    public DateTimeOffset? BreadthComputedAt { get; set; }

    public string Source { get; set; } = ContextDayStore.DerivedSource;

    public DateTimeOffset WrittenAt { get; set; }
}
