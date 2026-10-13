using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

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

    /// <summary>The previous session's close as the last quote reported it (ADR 0019); null until a quote has given one. A sync never touches it.</summary>
    public decimal? PreviousClose { get; private set; }

    /// <summary>Where <see cref="LastPrice"/> came from: the holdings list at the last sync, or a quote.</summary>
    public PriceSource LastPriceSource { get; private set; } = PriceSource.Sync;

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

        // The previous close belongs to the session of the quote that gave it: a holding that comes back must not show a day change against it.
        PreviousClose = null;
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

    /// <summary>
    /// A market quote for the holding (ADR 0019 §3): the price, the previous session's close and the time of the fetch, source <see cref="PriceSource.Quote"/>. A closed holding is not priced, and a last price that is
    /// zero or negative is no quote at all (nothing changes, false). A previous close that is missing, zero or negative is ignored: the one held stays. The batch update of the store applies the same rules
    /// to many holdings in one statement (<c>IHoldingQuotes.ApplyAsync</c>).
    /// </summary>
    public bool ApplyQuote(decimal last, decimal? previousClose, DateTimeOffset at)
    {
        if (Status != HoldingStatus.Open || last <= 0)
        {
            return false;
        }

        LastPrice = Round(last);
        LastPriceAt = at;
        LastPriceSource = PriceSource.Quote;
        if (previousClose is > 0)
        {
            PreviousClose = Round(previousClose.Value);
        }

        return true;
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

    /// <summary>
    /// True when the lots carry facts from trades (ADR 0023): a lot made from a buy, or a piece closed at a real sale price. Such a holding was rebuilt from its trades; the re-dating of the default-dated lots leaves it alone
    /// and the date of its opening lot must stay before its first trade.
    /// </summary>
    public bool HasTradeLots => _lots.Any(l => l.Source == LotSource.Trade || (l.ClosedOn is not null && !l.Estimated));

    /// <summary>The day of the earliest trade the lots show (a trade lot's date, or the day a really-sold piece was closed); null when the holding has no trade lots.</summary>
    public DateOnly? EarliestTradeDate => _lots
        .Where(l => l.Source == LotSource.Trade || (l.ClosedOn is not null && !l.Estimated))
        .Select(l => l.Source == LotSource.Trade ? l.AcquiredOn : l.ClosedOn!.Value)
        .Cast<DateOnly?>()
        .Min();

    /// <summary>
    /// Sets the acquisition date of one lot (checked against <paramref name="today"/>, India). A lot made from a trade takes its date from the trade and refuses (<c>portfolio.lot_from_trade</c>); on a holding
    /// rebuilt from trades the opening lot (imported, not made from a trade) must stay before the earliest trade.
    /// </summary>
    public Result ChangeLotDate(Guid lotId, DateOnly date, DateOnly today)
    {
        var lot = _lots.FirstOrDefault(l => l.Id == lotId);
        if (lot is null)
        {
            return Result.Failure(PortfolioErrors.LotNotFound);
        }

        if (lot.Source == LotSource.Trade)
        {
            return Result.Failure(PortfolioErrors.LotFromTrade);
        }

        if (lot.Source == LotSource.Import && EarliestTradeDate is { } earliest && date >= earliest)
        {
            return Result.Failure(PortfolioErrors.Invalid("acquiredOn", $"The opening lot is from before the first trade: choose a date before {earliest:yyyy-MM-dd}."));
        }

        return lot.ChangeDate(date, today);
    }

    /// <summary>
    /// Replaces all lots by the plan of a rebuild from trades (ADR 0023): the old lots are removed, the planned ones created in the planned order (each a microsecond apart, so that first-in first-out stays stable).
    /// The open quantities of the plan must add up to the broker's quantity; a closed piece is closed on or after its acquisition day, at a real (not estimated) price.
    /// </summary>
    public void ReplaceLots(IReadOnlyList<LotPlan> plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (Status != HoldingStatus.Open)
        {
            throw new InvalidOperationException("Lots of a closed holding are not rebuilt.");
        }

        if (plan.Where(p => p.IsOpen).Sum(p => p.Qty) != Qty)
        {
            throw new ArgumentException("The open quantities of the plan must add up to the holding's quantity.", nameof(plan));
        }

        var fresh = new List<HoldingLot>(plan.Count);
        for (var i = 0; i < plan.Count; i++)
        {
            var step = plan[i];
            var lot = HoldingLot.Open(Id, step.AcquiredOn, step.Qty, Round(step.UnitCost), step.Source, step.UsesDefaultDate, now.AddTicks(i * TimeSpan.TicksPerMicrosecond));
            if (step is { ClosedOn: { } closedOn, ClosePrice: { } closePrice })
            {
                if (closedOn < step.AcquiredOn)
                {
                    throw new ArgumentException("A piece cannot be closed before it was acquired.", nameof(plan));
                }

                lot.Close(closedOn, Round(closePrice), estimated: false);
            }
            else if (!step.IsOpen)
            {
                throw new ArgumentException("A closed piece has a close price.", nameof(plan));
            }

            fresh.Add(lot);
        }

        _lots.Clear();
        _lots.AddRange(fresh);
    }

    /// <summary>
    /// The account's first import date changed (ADR 0015): every lot that still carries it (imported, flagged as default-dated) gets <paramref name="date"/>
    /// and stays flagged. A closed lot whose close day is before <paramref name="date"/> cannot take it and is counted as skipped. Lots the owner dated,
    /// lots a refresh found and manual lots are never touched. The caller has checked the date (not in the future, not before 1990-01-01).
    /// </summary>
    public LotRedating RedateDefaultLots(DateOnly date)
    {
        var redated = 0;
        var skipped = 0;
        if (HasTradeLots)
        {
            // Rebuilt from trades (ADR 0023): the opening lot was dated before the first trade, a later first import date must not move it. Counted as skipped.
            return new LotRedating(0, _lots.Count(l => l.Source == LotSource.Import && l.UsesDefaultDate));
        }

        foreach (var lot in _lots.Where(l => l.Source == LotSource.Import && l.UsesDefaultDate))
        {
            if (lot.RedateDefault(date))
            {
                redated++;
            }
            else
            {
                skipped++;
            }
        }

        return new LotRedating(redated, skipped);
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

    /// <summary>
    /// The price from a holdings sync (ADR 0019 §3): a price of zero or less is no price. A quote of the same India day stays — it is newer than anything the sync knows — otherwise the broker's price is taken, source
    /// <see cref="PriceSource.Sync"/>. The previous close is never touched.
    /// </summary>
    private void SetPrice(decimal price, DateTimeOffset now)
    {
        if (price <= 0 || (LastPriceSource == PriceSource.Quote && LastPriceAt is { } at && IndiaTime.Today(at) == IndiaTime.Today(now)))
        {
            return;
        }

        LastPrice = Round(price);
        LastPriceAt = now;
        LastPriceSource = PriceSource.Sync;
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
