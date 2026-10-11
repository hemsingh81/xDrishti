using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// What one account holds of one instrument, as the broker reports it (quantity, average cost, last price), plus the lots that say when and at
/// what cost the shares were bought. The broker's quantity and average cost are always what is stored; the lots are the owner's story of how
/// it came about (ADR 0012): their open quantities add up to <see cref="Qty"/>. A holding the broker stops listing is closed, not deleted.
/// </summary>
public sealed class Holding : Entity<Guid>
{
    public const int MaxExchangeLength = 16;

    public const int MaxSecurityIdLength = 32;

    public const int MaxIsinLength = 16;

    public const int MaxSymbolLength = 64;

    public const int MaxNameLength = 200;

    private readonly List<HoldingLot> _lots = [];

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private Holding(
        Guid id,
        Guid accountId,
        Guid bucketId,
        string exchange,
        string securityId,
        string tradingSymbol,
        string name,
        DateTimeOffset firstSeenAt)
        : base(id)
    {
        AccountId = accountId;
        BucketId = bucketId;
        Exchange = exchange;
        SecurityId = securityId;
        TradingSymbol = tradingSymbol;
        Name = name;
        FirstSeenAt = firstSeenAt;
        SyncedAt = firstSeenAt;
    }

    public Guid AccountId { get; private set; }

    public Guid BucketId { get; private set; }

    /// <summary>The instrument in the master list; null when the holding cannot be mapped (for example BSE only).</summary>
    public Guid? InstrumentId { get; private set; }

    public string Exchange { get; private set; }

    public string SecurityId { get; private set; }

    public string? Isin { get; private set; }

    public string TradingSymbol { get; private set; }

    public string Name { get; private set; }

    /// <summary>The broker's quantity at the last sync; 0 once the holding is closed.</summary>
    public long Qty { get; private set; }

    /// <summary>The broker's average cost per share at the last sync.</summary>
    public decimal AvgCost { get; private set; }

    public decimal LastPrice { get; private set; }

    public DateTimeOffset? LastPriceAt { get; private set; }

    public HoldingStatus Status { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public IReadOnlyList<HoldingLot> Lots => _lots;

    public bool IsMapped => InstrumentId is not null;

    /// <summary>A holding the owner imports: quantity and cost from the broker, one lot with the owner's date.</summary>
    public static Holding Open(
        Guid accountId,
        Guid bucketId,
        Guid? instrumentId,
        string? name,
        BrokerHolding row,
        DateOnly acquiredOn,
        bool usesDefaultDate,
        LotSource source,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(row);
        var holding = new Holding(
            Guid.CreateVersion7(now), accountId, bucketId, Cut(row.Exchange, MaxExchangeLength), Cut(row.SecurityId, MaxSecurityIdLength),
            Cut(row.TradingSymbol, MaxSymbolLength), Cut(string.IsNullOrWhiteSpace(name) ? row.TradingSymbol : name, MaxNameLength), now);
        holding.Describe(row, instrumentId, name);
        holding.Begin(row, acquiredOn, usesDefaultDate, source, now);
        return holding;
    }

    /// <summary>Brings a closed holding back (the broker lists it again): open with one lot of the broker's quantity and cost.</summary>
    public void Reopen(BrokerHolding row, Guid? instrumentId, string? name, DateOnly acquiredOn, bool usesDefaultDate, LotSource source, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (Status == HoldingStatus.Open)
        {
            throw new InvalidOperationException("The holding is open already.");
        }

        Describe(row, instrumentId, name);
        Begin(row, acquiredOn, usesDefaultDate, source, now);
    }

    /// <summary>
    /// Brings the holding in line with what the broker reports now. A holding that was closed and is listed again comes back with one lot dated
    /// <paramref name="today"/>. Otherwise: a larger quantity adds a lot dated today at the implied cost of the added shares (the change in total
    /// cost divided by the added quantity; the last price when that is not positive); a smaller quantity closes lots oldest first at the last price,
    /// marked estimated; the same quantity only updates prices. Quantity and average cost are the broker's afterwards.
    /// </summary>
    public HoldingChange Reconcile(BrokerHolding row, Guid? instrumentId, string? name, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (Status == HoldingStatus.Closed)
        {
            Reopen(row, instrumentId, name, today, usesDefaultDate: false, LotSource.Added, now);
            return HoldingChange.Added;
        }

        var oldQty = Qty;
        var oldAvg = AvgCost;
        var lastKnown = LastPrice;
        Describe(row, instrumentId, name);
        var price = row.LastPrice > 0 ? row.LastPrice : lastKnown;
        var delta = row.Qty - oldQty;
        var change = HoldingChange.Unchanged;
        if (delta > 0)
        {
            var implied = ((row.Qty * row.AvgCost) - (oldQty * oldAvg)) / delta;
            var unitCost = implied > 0 ? implied : price > 0 ? price : row.AvgCost;
            _lots.Add(HoldingLot.Open(Id, today, delta, Round(unitCost), LotSource.Added, usesDefaultDate: false, now));
            change = HoldingChange.Increased;
        }
        else if (delta < 0)
        {
            CloseFifo(-delta, today, price > 0 ? price : oldAvg, estimated: true, now);
            change = HoldingChange.Decreased;
        }

        Qty = row.Qty;
        AvgCost = Round(row.AvgCost);
        SetPrice(row.LastPrice, now);
        SyncedAt = now;
        return change;
    }

    /// <summary>The broker no longer lists the holding: all lots are closed oldest first at the last known price (estimated). False when it was closed already.</summary>
    public bool CloseMissing(DateOnly today, DateTimeOffset now)
    {
        if (Status == HoldingStatus.Closed)
        {
            return false;
        }

        CloseFifo(Qty, today, LastPrice > 0 ? LastPrice : AvgCost, estimated: true, now);
        Qty = 0;
        Status = HoldingStatus.Closed;
        ClosedAt = now;
        SyncedAt = now;
        return true;
    }

    /// <summary>Sets the acquisition date of one lot (checked against <paramref name="today"/>, India).</summary>
    public Result ChangeLotDate(Guid lotId, DateOnly date, DateOnly today)
    {
        var lot = _lots.FirstOrDefault(l => l.Id == lotId);
        return lot is null ? Result.Failure(PortfolioErrors.LotNotFound) : lot.ChangeDate(date, today);
    }

    /// <summary>The open lots, oldest first (the order in which a sale closes them).</summary>
    public IEnumerable<HoldingLot> OpenLotsOldestFirst() =>
        _lots.Where(l => l.IsOpen).OrderBy(l => l.AcquiredOn).ThenBy(l => l.CreatedAt).ThenBy(l => l.Id);

    private void Begin(BrokerHolding row, DateOnly acquiredOn, bool usesDefaultDate, LotSource source, DateTimeOffset now)
    {
        _lots.Add(HoldingLot.Open(Id, acquiredOn, row.Qty, Round(row.AvgCost), source, usesDefaultDate, now));
        Qty = row.Qty;
        AvgCost = Round(row.AvgCost);
        Status = HoldingStatus.Open;
        ClosedAt = null;
        SetPrice(row.LastPrice, now);
        SyncedAt = now;
    }

    private void Describe(BrokerHolding row, Guid? instrumentId, string? name)
    {
        InstrumentId = instrumentId;
        Isin = string.IsNullOrWhiteSpace(row.Isin) ? null : Cut(row.Isin.Trim().ToUpperInvariant(), MaxIsinLength);
        TradingSymbol = Cut(row.TradingSymbol, MaxSymbolLength);
        Name = Cut(string.IsNullOrWhiteSpace(name) ? row.TradingSymbol : name, MaxNameLength);
    }

    private void SetPrice(decimal price, DateTimeOffset now)
    {
        if (price > 0)
        {
            LastPrice = Round(price);
            LastPriceAt = now;
        }
    }

    private void CloseFifo(long qty, DateOnly on, decimal price, bool estimated, DateTimeOffset now)
    {
        var remaining = qty;
        foreach (var lot in OpenLotsOldestFirst().ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, lot.Qty);
            var closed = lot.CloseQuantity(take, on, Round(price), estimated, now);
            if (!ReferenceEquals(closed, lot))
            {
                _lots.Add(closed);
            }

            remaining -= take;
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
