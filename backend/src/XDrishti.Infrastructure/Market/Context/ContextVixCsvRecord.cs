namespace XDrishti.Infrastructure.Market.Context;

/// <summary>A row of <c>market.context_vix_csv</c> (P3-01d fallback D): the INDIA VIX close of one day read from an owner-dropped NSE CSV. Insert-only; the day store reads it only for a day whose own VIX is missing.</summary>
internal sealed class ContextVixCsvRecord
{
    public DateOnly Date { get; set; }

    public double VixClose { get; set; }

    public string Source { get; set; } = ContextDayStore.CsvSource;

    public DateTimeOffset WrittenAt { get; set; }
}
