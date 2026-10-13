using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Read models of holdings for the calculator and the query tests (made-up symbols and prices).</summary>
internal static class HoldingFixtures
{
    public static readonly DateOnly Valuation = new(2026, 10, 11);

    public static readonly Guid Account = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    public static readonly Guid Bucket = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    public static LotData Open(long qty, decimal cost, DateOnly acquiredOn, bool usesDefaultDate = false, string source = "import") =>
        new(Guid.NewGuid(), acquiredOn, qty, cost, source, usesDefaultDate, null, null, false);

    public static LotData Closed(long qty, decimal cost, DateOnly acquiredOn, DateOnly closedOn, decimal closePrice, bool estimated = true) =>
        new(Guid.NewGuid(), acquiredOn, qty, cost, "import", false, closedOn, closePrice, estimated);

    public static HoldingData Holding(decimal lastPrice, string symbol = "EXMPL", string status = "open", Guid? instrument = null, params LotData[] lots) =>
        new(
            Guid.NewGuid(), Account, "Example One", Bucket, "Example One · Portfolio", instrument, instrument is null ? null : symbol, "ALL", "1001", "INE000A01011",
            symbol, symbol, lots.Where(l => l.IsOpen).Sum(l => l.Qty), 0m, lastPrice, new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.Zero), status,
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null, new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.Zero), lots);

    /// <summary>The holding as a quote left it: the previous close and the source of the price.</summary>
    public static HoldingData Quoted(this HoldingData holding, decimal? previousClose, string source = HoldingData.QuoteSource) =>
        holding with { PreviousClose = previousClose, PriceSource = source };
}
