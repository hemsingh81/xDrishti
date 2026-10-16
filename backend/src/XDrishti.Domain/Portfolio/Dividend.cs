using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// A dividend the owner received (ADR 0026), entered by hand: the rupees credited on an India <see cref="PayDate"/>, for a holding of the account or, when the holding is gone or unknown, for the account alone (<see cref="HoldingId"/> null: unallocated).
/// Immutable except for being voided: a mistake is voided and entered again, never edited, and the runtime role cannot delete it. The instrument, ISIN and symbol are copied from the holding when it is created, so the dividend still reads well later.
/// <see cref="Note"/> is the owner's free text: plain text only, length-capped. A dividend read from a file (<see cref="DividendSource.Csv"/>, ADR 0028) also has the <see cref="BatchId"/> of its import, its <see cref="ExternalId"/> (the identity that
/// makes the same row never stored twice), the file's <see cref="Quantity"/> and, when it is not allocated to a holding, the file's <see cref="ScripName"/> as plain text.
/// </summary>
public sealed class Dividend : Entity<Guid>
{
    public const int MaxCreatedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private Dividend(
        Guid id,
        Guid accountId,
        Guid? holdingId,
        Guid? instrumentId,
        string? isin,
        string? tradingSymbol,
        DateOnly payDate,
        decimal amount,
        decimal? perShare,
        string? note,
        DividendSource source,
        string createdBy,
        DateTimeOffset createdAt,
        Guid? batchId,
        string? externalId,
        long? quantity,
        string? scripName)
        : base(id)
    {
        AccountId = accountId;
        HoldingId = holdingId;
        InstrumentId = instrumentId;
        Isin = isin;
        TradingSymbol = tradingSymbol;
        PayDate = payDate;
        Amount = amount;
        PerShare = perShare;
        Note = note;
        Source = source;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        BatchId = batchId;
        ExternalId = externalId;
        Quantity = quantity;
        ScripName = scripName;
    }

    public Guid AccountId { get; private set; }

    /// <summary>The holding the dividend is for; null when it is not allocated to a holding.</summary>
    public Guid? HoldingId { get; private set; }

    public Guid? InstrumentId { get; private set; }

    public string? Isin { get; private set; }

    public string? TradingSymbol { get; private set; }

    /// <summary>The India date the money was credited.</summary>
    public DateOnly PayDate { get; private set; }

    /// <summary>Rupees received.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Rupees per share, when the owner knows it (informational).</summary>
    public decimal? PerShare { get; private set; }

    public string? Note { get; private set; }

    public DividendSource Source { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidedBy { get; private set; }

    /// <summary>The import (file) that made the dividend; null for one typed in.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>The identity of a dividend read from a file (unique per account among the dividends that are not voided); null for one typed in.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>The number of shares the file says the dividend was paid on; null when the file has none or the dividend was typed in.</summary>
    public long? Quantity { get; private set; }

    /// <summary>The scrip name as the file wrote it, for a dividend not allocated to a holding; plain text. Null otherwise.</summary>
    public string? ScripName { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    public static Dividend Create(
        Guid accountId,
        Guid? holdingId,
        Guid? instrumentId,
        string? isin,
        string? tradingSymbol,
        DateOnly payDate,
        decimal amount,
        decimal? perShare,
        string? note,
        string createdBy,
        DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A dividend belongs to an account.", nameof(accountId));
        }

        var rounded = DividendRules.RoundAmount(amount);
        if (DividendRules.AmountProblem(rounded) is { } amountProblem)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amountProblem);
        }

        decimal? share = null;
        if (perShare is { } given)
        {
            share = DividendRules.RoundPerShare(given);
            if (DividendRules.PerShareProblem(share.Value) is { } shareProblem)
            {
                throw new ArgumentOutOfRangeException(nameof(perShare), shareProblem);
            }
        }

        var text = DividendRules.CleanNote(note);
        if (text is { Length: > DividendRules.MaxNoteLength })
        {
            throw new ArgumentException($"The note has at most {DividendRules.MaxNoteLength} characters.", nameof(note));
        }

        var code = TradeRules.NormaliseIsin(isin);
        var symbol = TradeRules.NormaliseSymbol(tradingSymbol);
        return new Dividend(
            Guid.CreateVersion7(now),
            accountId,
            holdingId,
            instrumentId,
            code is null ? null : Cut(code, Holding.MaxIsinLength),
            symbol.Length == 0 ? null : Cut(symbol, Holding.MaxSymbolLength),
            payDate,
            rounded,
            share,
            text,
            DividendSource.Manual,
            Actor(createdBy),
            now,
            null,
            null,
            null,
            null);
    }

    /// <summary>
    /// A dividend read from a file (ADR 0028): the amount is rupees credited (above 0, to the paisa), <paramref name="perShare"/> and <paramref name="quantity"/> are the file's when it has them (a per-share amount of 0 is not kept),
    /// <paramref name="scripName"/> is kept only when the dividend belongs to no holding. <paramref name="externalId"/> is the identity under which the account stores the row once.
    /// </summary>
    public static Dividend FromFile(
        Guid accountId,
        Guid? holdingId,
        Guid? instrumentId,
        string? isin,
        string? tradingSymbol,
        string? scripName,
        DateOnly payDate,
        decimal amount,
        decimal? perShare,
        long? quantity,
        Guid batchId,
        string externalId,
        string createdBy,
        DateTimeOffset now)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("A dividend of a file belongs to a batch.", nameof(batchId));
        }

        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > DividendRules.MaxExternalIdLength)
        {
            throw new ArgumentException($"The identity is 1 to {DividendRules.MaxExternalIdLength} characters.", nameof(externalId));
        }

        if (quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "The quantity is above 0 when given.");
        }

        var manual = Create(accountId, holdingId, instrumentId, isin, tradingSymbol, payDate, amount, perShare is > 0m ? perShare : null, null, createdBy, now);
        var name = holdingId is null ? DividendRules.CleanScripName(scripName) : null;
        return new Dividend(
            manual.Id,
            manual.AccountId,
            manual.HoldingId,
            manual.InstrumentId,
            manual.Isin,
            manual.TradingSymbol,
            manual.PayDate,
            manual.Amount,
            manual.PerShare,
            null,
            DividendSource.Csv,
            manual.CreatedBy,
            now,
            batchId,
            externalId,
            quantity,
            name);
    }

    /// <summary>Voids the dividend (it stops counting, it is never deleted). False when it was voided already.</summary>
    public bool Void(string by, DateTimeOffset at)
    {
        if (VoidedAt is not null)
        {
            return false;
        }

        VoidedBy = Actor(by);
        VoidedAt = at;
        return true;
    }

    private static string Actor(string? by)
    {
        var actor = by?.Trim();
        return string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= MaxCreatedByLength ? actor : actor[..MaxCreatedByLength];
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
