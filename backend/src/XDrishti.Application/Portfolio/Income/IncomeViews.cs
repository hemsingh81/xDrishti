using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>Builds the response shape of the income figures; the rate is a percent number rounded to four decimals, as the gross XIRR is.</summary>
internal static class IncomeViews
{
    public static IncomeView? View(IncomeFigures? figures) => figures is null
        ? null
        : new IncomeView(
            figures.Dividends,
            figures.Charges,
            figures.LedgerCharges,
            figures.Unallocated,
            figures.TotalReturnPct,
            figures.NetTotalReturnPct,
            figures.NetXirr is { State: XirrState.Ok, Rate: { } rate } ? Math.Round((decimal)(rate * 100.0), 4, MidpointRounding.AwayFromZero) : null,
            XirrStateCodes.Name(figures.NetXirr.State),
            figures.CoverageFrom,
            figures.DividendsFrom,
            figures.ChargesFrom,
            figures.TradeChargesFrom,
            figures.LedgerChargesFrom);
}
