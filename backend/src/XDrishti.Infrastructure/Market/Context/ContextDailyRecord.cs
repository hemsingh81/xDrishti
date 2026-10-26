namespace XDrishti.Infrastructure.Market.Context;

/// <summary>A row of <c>market.context_daily</c> (P3-01d): one India trading day of the regime index (high, low, close) and the volatility index close, derived from the stored 1-minute bars. Insert-only for the runtime role.</summary>
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

    public string Source { get; set; } = ContextDayStore.DerivedSource;

    public DateTimeOffset WrittenAt { get; set; }
}
