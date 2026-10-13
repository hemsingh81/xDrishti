using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>Builds the response shapes from holdings and their returns. The wire names of the enums are the lower-case names.</summary>
internal static class HoldingViews
{
    public static ReturnsView Returns(ReturnFigures figures) => new(
        figures.Invested,
        figures.Value,
        figures.Pnl,
        figures.PnlPct,
        figures.Xirr is { State: XirrState.Ok, Rate: { } rate } ? Math.Round((decimal)(rate * 100.0), 4, MidpointRounding.AwayFromZero) : null,
        XirrStateCodes.Name(figures.Xirr.State),
        figures.RealisedPnl,
        figures.RealisedEstimated);

    public static HoldingRow Row(HoldingData holding, DateOnly valuationDate)
    {
        var open = holding.Lots.Where(l => l.IsOpen).ToList();
        var dayChange = DayChangeCalculator.ForHolding(holding);
        return new HoldingRow(
            holding.Id,
            holding.AccountId,
            holding.AccountName,
            holding.BucketId,
            holding.BucketName,
            holding.InstrumentId,
            holding.CanonicalSymbol,
            holding.InstrumentId is not null,
            holding.Exchange,
            holding.SecurityId,
            holding.Isin,
            holding.TradingSymbol,
            holding.Name,
            holding.Qty,
            holding.AvgCost,
            holding.LastPrice,
            holding.LastPriceAt,
            holding.Status,
            holding.FirstSeenAt,
            holding.ClosedAt,
            holding.SyncedAt,
            open.Count,
            open.Count == 0 ? null : open.Min(l => l.AcquiredOn),
            open.Any(l => l.UsesDefaultDate),
            holding.IsPriced,
            Returns(ReturnsCalculator.ForHolding(holding, valuationDate)),
            holding.PreviousClose,
            dayChange?.Amount,
            dayChange?.Pct,
            holding.LastPriceAt,
            holding.PriceSource);
    }

    public static PricesAsOf PriceTimes(IEnumerable<HoldingData> holdings)
    {
        var times = holdings.Where(h => h.IsOpen && h.IsPriced && h.LastPriceAt is not null).Select(h => h.LastPriceAt!.Value).ToList();
        return times.Count == 0 ? new PricesAsOf(null, null) : new PricesAsOf(times.Min(), times.Max());
    }

    public static LotRow Lot(HoldingData holding, LotData lot, DateOnly valuationDate) => new(
        lot.Id,
        lot.AcquiredOn,
        lot.Qty,
        lot.UnitCost,
        lot.Source,
        lot.UsesDefaultDate,
        lot.IsOpen ? "open" : "closed",
        lot.ClosedOn,
        lot.ClosePrice,
        lot.Estimated,
        Returns(ReturnsCalculator.ForLot(holding, lot, valuationDate)));

    public static SyncRunView Run(HoldingSync run) => new(
        run.Id,
        run.AccountId,
        Name(run.Trigger),
        Name(run.Status),
        run.StartedAt,
        run.FinishedAt,
        run.NewCount,
        run.IncreasedCount,
        run.DecreasedCount,
        run.ClosedCount,
        run.UnchangedCount,
        run.Error);

    public static string Name(SyncTrigger trigger) => trigger.ToString().ToLowerInvariant();

    public static string Name(SyncStatus status) => status.ToString().ToLowerInvariant();
}
