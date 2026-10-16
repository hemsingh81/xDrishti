using XDrishti.Application.Live.Push;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Application.Tests.Portfolio.Live.Golden;

/// <summary>The golden file (<c>frontend/src/test/fixtures/liveGolden.json</c>): what the real server calculators make of made-up holdings, before and after prices pushed over the hub.</summary>
internal sealed record GoldenFile(int Schema, string Note, DateOnly ValuationDate, IReadOnlyList<GoldenScenario> Scenarios);

/// <summary>
/// One case: the holdings, the dividends and costs recorded for them and the pushes (inputs), and what the server answers before the pushes (what the browser has read) and after them (what the browser must work out). The income does not change with a
/// push; what does is the total return % (it holds the unrealised profit), which the browser works out again from the figures of the rows, groups and tiles it has read. Net XIRR is null in every answer, like XIRR; its state is the server's.
/// </summary>
internal sealed record GoldenScenario(
    string Name,
    string Description,
    IReadOnlyList<GoldenHolding> Holdings,
    GoldenIncome Income,
    IReadOnlyList<InstrumentPrice> Pushes,
    GoldenState Before,
    GoldenState After);

/// <summary>
/// The dividends and costs recorded for the accounts of a case, as the income port reads them (ADR 0026): the dividends that are not voided (<c>HoldingId</c> null: unallocated), the charges of trades per day (matched to a holding by symbol; a symbol
/// that is none of the account's holdings belongs to the account), the ledger's costs and the first date of each account's ledger. <c>ExtraAccounts</c> are accounts with no holding.
/// </summary>
internal sealed record GoldenIncome(
    IReadOnlyList<GoldenDividend> Dividends,
    IReadOnlyList<GoldenTradeCharge> TradeCharges,
    IReadOnlyList<GoldenLedgerCharge> LedgerCharges,
    IReadOnlyList<GoldenLedgerStart> LedgerStarts,
    IReadOnlyList<Guid> ExtraAccounts)
{
    public static GoldenIncome None { get; } = new([], [], [], [], []);
}

internal sealed record GoldenDividend(Guid AccountId, Guid? HoldingId, DateOnly PayDate, decimal Amount);

internal sealed record GoldenTradeCharge(Guid AccountId, string Symbol, DateOnly TradeDate, decimal Amount);

internal sealed record GoldenLedgerCharge(Guid AccountId, DateOnly Date, decimal Amount);

internal sealed record GoldenLedgerStart(Guid AccountId, DateOnly From);

/// <summary>A holding as the server stores it (the input of the calculators). <c>Sector</c> is the master's sector of the instrument, "Unclassified" when it has none. Prices have at most four decimals, as the columns do.</summary>
internal sealed record GoldenHolding(
    Guid Id,
    Guid AccountId,
    Guid BucketId,
    Guid? InstrumentId,
    string Sector,
    string Symbol,
    long Qty,
    string Status,
    decimal LastPrice,
    DateTimeOffset? LastPriceAt,
    string PriceSource,
    decimal? PreviousClose,
    DateTimeOffset? PreviousCloseAt,
    IReadOnlyList<GoldenLot> Lots);

internal sealed record GoldenLot(long Qty, decimal UnitCost, DateOnly AcquiredOn, DateOnly? ClosedOn, decimal? ClosePrice);

/// <summary>
/// What the API answers for the holdings: every holding as a row (input order), the summary of all of them, and the groups by account, bucket and sector for the status "all" and for "open". <c>SectorMembers</c> is the member
/// list of each sector group (open holdings, by group key) as the members endpoint answers.
/// </summary>
internal sealed record GoldenState(
    IReadOnlyList<HoldingRow> Rows,
    HoldingsSummary Summary,
    GoldenGroupings Groups,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> SectorMembers);

internal sealed record GoldenGroupings(GoldenGroupSet All, GoldenGroupSet Open);

internal sealed record GoldenGroupSet(HoldingGroups Account, HoldingGroups Bucket, HoldingGroups Sector);
