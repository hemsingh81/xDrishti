using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// A part of a holding bought on one day at one cost. It is open until it is closed (sold) as a whole, at one price on one day. A partial sale
/// splits a lot into a closed piece and an open remainder with the same date and cost, so every lot has at most one close price.
/// </summary>
public sealed class HoldingLot : Entity<Guid>
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private HoldingLot(
        Guid id,
        Guid holdingId,
        DateOnly acquiredOn,
        long qty,
        decimal unitCost,
        LotSource source,
        bool usesDefaultDate,
        DateTimeOffset createdAt)
        : base(id)
    {
        HoldingId = holdingId;
        AcquiredOn = acquiredOn;
        Qty = qty;
        UnitCost = unitCost;
        Source = source;
        UsesDefaultDate = usesDefaultDate;
        CreatedAt = createdAt;
    }

    public Guid HoldingId { get; private set; }

    public DateOnly AcquiredOn { get; private set; }

    public long Qty { get; private set; }

    public decimal UnitCost { get; private set; }

    public LotSource Source { get; private set; }

    /// <summary>The owner did not choose the date: it came from the import's default date. Cleared when the owner edits the date.</summary>
    public bool UsesDefaultDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The day the lot was closed; null while it is open.</summary>
    public DateOnly? ClosedOn { get; private set; }

    public decimal? ClosePrice { get; private set; }

    /// <summary>The sale price is the last price the sync saw, not the real sale price.</summary>
    public bool Estimated { get; private set; }

    public bool IsOpen => ClosedOn is null;

    internal static HoldingLot Open(
        Guid holdingId, DateOnly acquiredOn, long qty, decimal unitCost, LotSource source, bool usesDefaultDate, DateTimeOffset now)
    {
        if (qty <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(qty), "A lot has a positive quantity.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(unitCost);
        return new HoldingLot(Guid.CreateVersion7(now), holdingId, acquiredOn, qty, unitCost, source, usesDefaultDate, now);
    }

    /// <summary>Closes the whole lot.</summary>
    internal void Close(DateOnly on, decimal price, bool estimated)
    {
        ClosedOn = on;
        ClosePrice = price;
        Estimated = estimated;
    }

    /// <summary>
    /// Closes <paramref name="qty"/> shares of this open lot. The whole lot when <paramref name="qty"/> is its quantity (returns this lot);
    /// otherwise a new closed lot of that size is split off and this lot keeps the rest (returns the new lot).
    /// </summary>
    internal HoldingLot CloseQuantity(long qty, DateOnly on, decimal price, bool estimated, DateTimeOffset now)
    {
        if (qty >= Qty)
        {
            Close(on, price, estimated);
            return this;
        }

        var piece = Open(HoldingId, AcquiredOn, qty, UnitCost, Source, UsesDefaultDate, now);
        piece.Close(on, price, estimated);
        Qty -= qty;
        return piece;
    }

    /// <summary>
    /// Moves the lot to the account's new first import date (ADR 0015) and keeps it flagged as using the default date. Only a lot that still carries the
    /// first import date qualifies (imported, flagged); a closed lot whose close day is before the new date is left as it is. False when the lot was not changed.
    /// </summary>
    internal bool RedateDefault(DateOnly date)
    {
        if (Source != LotSource.Import || !UsesDefaultDate || (ClosedOn is { } closed && date > closed))
        {
            return false;
        }

        AcquiredOn = date;
        return true;
    }

    /// <summary>Gives the lot the date the owner chose. It no longer counts as using the default date.</summary>
    internal Result ChangeDate(DateOnly date, DateOnly today)
    {
        var errors = new FieldErrors();
        HoldingRules.CheckDate(errors, "acquiredOn", date, today);
        if (errors.HasErrors)
        {
            return Result.Failure(PortfolioErrors.Invalid(errors));
        }

        if (ClosedOn is { } closed && date > closed)
        {
            return Result.Failure(PortfolioErrors.Invalid("acquiredOn", "Cannot be after the day the lot was closed."));
        }

        AcquiredOn = date;
        UsesDefaultDate = false;
        return Result.Success();
    }
}
