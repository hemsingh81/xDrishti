using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>
/// Dividends, charges and the net returns of a scope (ADR 0026, docs/design/12 §4). Pure: no I/O, no clock. The gross figures (<see cref="ReturnFigures"/>) are not changed; this adds to them:
/// <list type="bullet">
/// <item>dividends and charges are the sums of what the book holds for the scope's holdings plus, for the accounts in <c>accountLevel</c> only, what belongs to the account and no holding (unallocated dividends, the ledger's costs of the account as such, charges of trades of no holding); the ledger part is also given on its own;</item>
/// <item>total return % = (realised + unrealised + dividends) over invested, net total return % = the same minus the charges (rounded once, like the gross P&amp;L %); both null without invested capital or without a usable price;</item>
/// <item>net XIRR = the XIRR of the lot cash flows of the priced holdings (the gross ones) plus their dividends as money in and their charges as money out on their dates, plus the account-level ones, with the same rules for insufficient, no data and not meaningful;</item>
/// <item>nothing at all (no dividend and no charge recorded) is no figures: null;</item>
/// <item>coverage: <c>DividendsFrom</c>, <c>TradeChargesFrom</c> (the earliest trade with charges in the scope: a trade without charges is unknown, not free, because CSV, manual and early broker rows carry none) and <c>LedgerChargesFrom</c> (the earliest ledger date of the account-level accounts) are each given when their source exists; <c>ChargesFrom</c> is the later of the last two when both exist; <c>CoverageFrom</c> is the later start of the sources that exist, never earlier than any of them.</item>
/// </list>
/// </summary>
internal static class IncomeCalculator
{
    private const int Decimals = 4;

    public static IncomeFigures? Of(ReturnFigures gross, IReadOnlyList<HoldingData> holdings, IncomeBook book, IReadOnlyCollection<Guid> accountLevel, DateOnly valuationDate)
    {
        ArgumentNullException.ThrowIfNull(gross);
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(accountLevel);

        var dividends = new List<CashFlow>();
        var tradeCharges = new List<CashFlow>();
        var ledgerCharges = new List<CashFlow>();
        var xirrDividends = new List<CashFlow>();
        var xirrCharges = new List<CashFlow>();
        foreach (var holding in holdings)
        {
            var own = book.Of(holding.Id);
            dividends.AddRange(own.Dividends);
            tradeCharges.AddRange(own.Charges);
            if (holding.IsPriced)
            {
                xirrDividends.AddRange(own.Dividends);
                xirrCharges.AddRange(own.Charges);
            }
        }

        decimal? unallocated = null;
        DateOnly? ledgerFrom = null;
        foreach (var accountId in accountLevel)
        {
            var account = book.Account(accountId);
            dividends.AddRange(account.Dividends);
            tradeCharges.AddRange(account.TradeCharges);
            ledgerCharges.AddRange(account.LedgerCharges);
            xirrDividends.AddRange(account.Dividends);
            xirrCharges.AddRange(account.TradeCharges);
            xirrCharges.AddRange(account.LedgerCharges);
            unallocated = (unallocated ?? 0m) + account.Dividends.Sum(f => f.Amount);
            ledgerFrom = Earlier(ledgerFrom, account.LedgerFrom);
        }

        if (dividends.Count == 0 && tradeCharges.Count == 0 && ledgerCharges.Count == 0)
        {
            return null;
        }

        var nothingPriced = holdings.Count > 0 && holdings.All(h => !h.IsPriced);
        var totalDividends = dividends.Sum(f => f.Amount);
        var totalCharges = tradeCharges.Sum(f => f.Amount) + ledgerCharges.Sum(f => f.Amount);
        var coverage = Coverage(dividends, tradeCharges, ledgerFrom);
        return new IncomeFigures(
            Round(totalDividends),
            Round(totalCharges),
            accountLevel.Count > 0 ? Round(ledgerCharges.Sum(f => f.Amount)) : null,
            unallocated is { } value ? Round(value) : null,
            nothingPriced ? null : Percent(gross, totalDividends),
            nothingPriced ? null : Percent(gross, totalDividends - totalCharges),
            NetXirr(holdings, nothingPriced, xirrDividends, xirrCharges, valuationDate),
            coverage.CoverageFrom,
            coverage.DividendsFrom,
            coverage.ChargesFrom,
            coverage.TradeChargesFrom,
            coverage.LedgerChargesFrom);
    }

    /// <summary>The dividends entered for a holding, summed; null when none was entered (not zero: "nothing entered" sorts and reads differently from "nothing received").</summary>
    public static decimal? DividendsOf(IncomeBook book, Guid holdingId)
    {
        ArgumentNullException.ThrowIfNull(book);
        var dividends = book.Of(holdingId).Dividends;
        return dividends.Count == 0 ? null : Round(dividends.Sum(f => f.Amount));
    }

    /// <summary>
    /// The figures of what belongs to accounts and to none of their holdings alone (the Unallocated row of a grouping): their unallocated dividends and costs. There is no invested capital to relate them to, so no percentages and no XIRR
    /// (<see cref="XirrState.NoData"/>). Null when the accounts have nothing recorded.
    /// </summary>
    public static IncomeFigures? OfAccounts(IncomeBook book, IReadOnlyCollection<Guid> accounts)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(accounts);
        var dividends = new List<CashFlow>();
        var tradeCharges = new List<CashFlow>();
        var ledgerCharges = new List<CashFlow>();
        DateOnly? ledgerFrom = null;
        foreach (var accountId in accounts)
        {
            var account = book.Account(accountId);
            dividends.AddRange(account.Dividends);
            tradeCharges.AddRange(account.TradeCharges);
            ledgerCharges.AddRange(account.LedgerCharges);
            ledgerFrom = Earlier(ledgerFrom, account.LedgerFrom);
        }

        if (dividends.Count == 0 && tradeCharges.Count == 0 && ledgerCharges.Count == 0)
        {
            return null;
        }

        var coverage = Coverage(dividends, tradeCharges, ledgerFrom);
        var total = Round(dividends.Sum(f => f.Amount));
        return new IncomeFigures(
            total,
            Round(tradeCharges.Sum(f => f.Amount) + ledgerCharges.Sum(f => f.Amount)),
            Round(ledgerCharges.Sum(f => f.Amount)),
            total,
            null,
            null,
            XirrResult.NoData,
            coverage.CoverageFrom,
            coverage.DividendsFrom,
            coverage.ChargesFrom,
            coverage.TradeChargesFrom,
            coverage.LedgerChargesFrom);
    }

    private static Dates Coverage(List<CashFlow> dividends, List<CashFlow> tradeCharges, DateOnly? ledgerFrom)
    {
        DateOnly? dividendsFrom = dividends.Count == 0 ? null : dividends.Min(f => f.Date);
        DateOnly? tradeChargesFrom = tradeCharges.Count == 0 ? null : tradeCharges.Min(f => f.Date);
        var chargesFrom = Later(tradeChargesFrom, ledgerFrom);
        return new Dates(Later(dividendsFrom, chargesFrom), dividendsFrom, chargesFrom, tradeChargesFrom, ledgerFrom);
    }

    private sealed record Dates(DateOnly? CoverageFrom, DateOnly? DividendsFrom, DateOnly? ChargesFrom, DateOnly? TradeChargesFrom, DateOnly? LedgerChargesFrom);

    private static decimal? Percent(ReturnFigures gross, decimal dividendsLessCharges) =>
        gross.Invested > 0m && gross.Pnl is { } unrealised
            ? Round((gross.RealisedPnl + unrealised + dividendsLessCharges) / gross.Invested * 100m)
            : null;

    private static XirrResult NetXirr(IReadOnlyList<HoldingData> holdings, bool nothingPriced, List<CashFlow> dividends, List<CashFlow> charges, DateOnly valuationDate)
    {
        if (nothingPriced)
        {
            return new XirrResult(XirrState.NoPrice, null);
        }

        var lots = ReturnsCalculator.FlowsOf(holdings.Where(h => h.IsPriced), valuationDate);
        if (lots.Count == 0)
        {
            return XirrResult.NoData;
        }

        List<CashFlow> flows = [.. lots, .. dividends, .. charges.Select(c => new CashFlow(c.Date, -c.Amount))];
        return Xirr.Compute(flows);
    }

    private static DateOnly? Earlier(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a < b ? a : b;

    /// <summary>The later of two dates; the one that exists when the other does not.</summary>
    private static DateOnly? Later(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a > b ? a : b;

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}
