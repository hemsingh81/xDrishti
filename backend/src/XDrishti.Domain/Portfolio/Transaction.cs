using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One trade of an account: a buy or a sell of a security on an India date, from a tradebook CSV, typed in by the owner (ADR 0023) or read from the broker's trade history (ADR 0025). Immutable except for being voided - a mistake is voided and
/// entered again, never edited - and, for a trade read from the broker, for being completed once with an ISIN, ticker or charges an earlier read left out (<see cref="EnrichFromBroker"/>); the runtime role cannot delete it. <see cref="ExternalId"/> is the trade's identity (the trade id of the file, a hash of the line, or a random id for a manual
/// trade); it is unique among the account's trades that are not voided. <see cref="Seq"/> is the line of the file (0 for a manual trade) and orders trades that tie on date, side and time.
/// </summary>
public sealed class Transaction : Entity<Guid>
{
    public const int MaxCreatedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private Transaction(
        Guid id,
        Guid accountId,
        Guid? instrumentId,
        string? isin,
        string tradingSymbol,
        string exchange,
        TradeSide side,
        DateOnly tradeDate,
        TimeOnly? tradeTime,
        long qty,
        decimal price,
        decimal charges,
        TradeSource source,
        Guid? batchId,
        int seq,
        string externalId,
        string createdBy,
        DateTimeOffset createdAt,
        Guid? runId)
        : base(id)
    {
        AccountId = accountId;
        InstrumentId = instrumentId;
        Isin = isin;
        TradingSymbol = tradingSymbol;
        Exchange = exchange;
        Side = side;
        TradeDate = tradeDate;
        TradeTime = tradeTime;
        Qty = qty;
        Price = price;
        Charges = charges;
        Source = source;
        BatchId = batchId;
        Seq = seq;
        ExternalId = externalId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        RunId = runId;
    }

    public Guid AccountId { get; private set; }

    /// <summary>The master-list instrument when the security could be mapped; null otherwise.</summary>
    public Guid? InstrumentId { get; private set; }

    public string? Isin { get; private set; }

    public string TradingSymbol { get; private set; }

    public string Exchange { get; private set; }

    public TradeSide Side { get; private set; }

    /// <summary>The India date of the trade.</summary>
    public DateOnly TradeDate { get; private set; }

    /// <summary>The India time of the trade; null when the source has none.</summary>
    public TimeOnly? TradeTime { get; private set; }

    /// <summary>Shares, as traded (not adjusted for later splits or bonuses).</summary>
    public long Qty { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>The charges of the trade as one total (rupees): not part of the cost or the lots, but counted as a cost in the net returns of the holding (ADR 0026).</summary>
    public decimal Charges { get; private set; }

    public TradeSource Source { get; private set; }

    public Guid? BatchId { get; private set; }

    public int Seq { get; private set; }

    public string ExternalId { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The broker history run (<c>portfolio.broker_sync_runs</c>) that read the trade; null for a trade from a file or typed in (ADR 0025).</summary>
    public Guid? RunId { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidedBy { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    public static Transaction Create(
        Guid accountId,
        Guid? instrumentId,
        string? isin,
        string tradingSymbol,
        string? exchange,
        TradeSide side,
        DateOnly tradeDate,
        TimeOnly? tradeTime,
        long qty,
        decimal price,
        decimal charges,
        TradeSource source,
        Guid? batchId,
        int seq,
        string externalId,
        string createdBy,
        DateTimeOffset now,
        Guid? runId = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A trade belongs to an account.", nameof(accountId));
        }

        var symbol = TradeRules.NormaliseSymbol(tradingSymbol);
        var code = TradeRules.NormaliseIsin(isin);
        if (symbol.Length == 0 && code is null)
        {
            throw new ArgumentException("A trade needs a symbol or an ISIN.", nameof(tradingSymbol));
        }

        if (TradeRules.QtyProblem(qty) is { } qtyProblem)
        {
            throw new ArgumentOutOfRangeException(nameof(qty), qtyProblem);
        }

        var rounded = TradeRules.Round(price);
        if (TradeRules.PriceProblem(rounded) is { } priceProblem)
        {
            throw new ArgumentOutOfRangeException(nameof(price), priceProblem);
        }

        var fees = TradeRules.Round(charges);
        if (TradeRules.ChargesProblem(fees) is { } chargesProblem)
        {
            throw new ArgumentOutOfRangeException(nameof(charges), chargesProblem);
        }

        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > TradeRules.MaxExternalIdLength)
        {
            throw new ArgumentException($"The external id has 1 to {TradeRules.MaxExternalIdLength} characters.", nameof(externalId));
        }

        var actor = createdBy?.Trim();
        actor = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= MaxCreatedByLength ? actor : actor[..MaxCreatedByLength];
        var venue = string.IsNullOrWhiteSpace(exchange) ? "NSE" : Cut(exchange.Trim().ToUpperInvariant(), Holding.MaxExchangeLength);
        return new Transaction(
            Guid.CreateVersion7(now),
            accountId,
            instrumentId,
            code is null ? null : Cut(code, Holding.MaxIsinLength),
            Cut(symbol, Holding.MaxSymbolLength),
            venue,
            side,
            tradeDate,
            tradeTime,
            qty,
            rounded,
            fees,
            source,
            batchId,
            seq,
            externalId,
            actor,
            now,
            runId);
    }

    /// <summary>
    /// Fills in what an earlier read of the broker's history left out of a trade that came from the broker (ADR 0025, 0026), from the same trade read again: the ISIN when the trade has none, the charges when they are 0 and the read has some, the ticker when
    /// it is empty and the read resolved one. Nothing else is ever changed - not the identity, side, date, quantity or price - and nothing is overwritten: a value that is there stays. A trade from a file or typed in, and a voided one, are left alone.
    /// True when something was filled in.
    /// </summary>
    public bool EnrichFromBroker(string? isin, string? tradingSymbol, decimal charges)
    {
        if (Source != TradeSource.Broker || VoidedAt is not null)
        {
            return false;
        }

        var changed = false;
        if (Isin is null && TradeRules.NormaliseIsin(isin) is { } code)
        {
            Isin = Cut(code, Holding.MaxIsinLength);
            changed = true;
        }

        var symbol = TradeRules.NormaliseSymbol(tradingSymbol);
        if (TradingSymbol.Length == 0 && symbol.Length > 0)
        {
            TradingSymbol = Cut(symbol, Holding.MaxSymbolLength);
            changed = true;
        }

        var fees = TradeRules.Round(charges);
        if (Charges == 0m && fees > 0m && TradeRules.ChargesProblem(fees) is null)
        {
            Charges = fees;
            changed = true;
        }

        return changed;
    }

    /// <summary>Voids the trade (it stops counting, it is never deleted). False when it was voided already.</summary>
    public bool Void(string by, DateTimeOffset at)
    {
        if (VoidedAt is not null)
        {
            return false;
        }

        var actor = by?.Trim();
        VoidedBy = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= MaxCreatedByLength ? actor : actor[..MaxCreatedByLength];
        VoidedAt = at;
        return true;
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
