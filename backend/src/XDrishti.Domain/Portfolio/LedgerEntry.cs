using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One line of the account's ledger at Dhan (ADR 0025): a voucher on an India date with one signed <see cref="Amount"/> (credit − debit, rupees; positive adds to the cash balance), the class decided from its texts, and Dhan's running balance when it gives
/// one. Immutable apart from being voided - only an entry imported from a report (<see cref="LedgerSource.Import"/>) is ever voided, by undoing its batch - and never deleted. <see cref="ExternalId"/> is the entry's identity (a hash of the account, date, voucher number, description, amount and the occurrence index), unique per account, so reading a window twice stores nothing new.
/// <see cref="Description"/> and <see cref="Narration"/> are Dhan's free text: untrusted, length-capped, to be shown as plain text only.
/// </summary>
public sealed class LedgerEntry : Entity<Guid>
{
    /// <summary>The reason recorded as the voider of an imported entry that Dhan's own ledger has replaced (ADR 0029).</summary>
    public const string SupersededReason = Transaction.SupersededReason;

    public const int MaxVoucherNoLength = 64;

    public const int MaxDescriptionLength = 200;

    public const int MaxNarrationLength = 500;

    public const int MaxExchangeLength = 32;

    public const int MaxExternalIdLength = 64;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private LedgerEntry(
        Guid id,
        Guid accountId,
        DateOnly entryDate,
        string voucherNo,
        string description,
        string narration,
        string? exchange,
        decimal amount,
        LedgerClass entryClass,
        decimal? runningBalance,
        string externalId,
        Guid? runId,
        DateTimeOffset createdAt,
        LedgerSource source,
        Guid? batchId,
        string? isin,
        long? quantity)
        : base(id)
    {
        AccountId = accountId;
        EntryDate = entryDate;
        VoucherNo = voucherNo;
        Description = description;
        Narration = narration;
        Exchange = exchange;
        Amount = amount;
        EntryClass = entryClass;
        RunningBalance = runningBalance;
        ExternalId = externalId;
        RunId = runId;
        CreatedAt = createdAt;
        Source = source;
        BatchId = batchId;
        Isin = isin;
        Quantity = quantity;
    }

    public Guid AccountId { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public string VoucherNo { get; private set; }

    public string Description { get; private set; }

    public string Narration { get; private set; }

    public string? Exchange { get; private set; }

    /// <summary>Credit minus debit, rupees.</summary>
    public decimal Amount { get; private set; }

    public LedgerClass EntryClass { get; private set; }

    public decimal? RunningBalance { get; private set; }

    public string ExternalId { get; private set; }

    /// <summary>The broker history run that read the entry; null for an entry imported from a report.</summary>
    public Guid? RunId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public LedgerSource Source { get; private set; }

    /// <summary>The import batch (<c>portfolio.cost_batches</c>) of an entry imported from a report; null for an entry read from Dhan.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>The security a depository charge was levied for (imported entries only).</summary>
    public string? Isin { get; private set; }

    /// <summary>The shares of the sale a depository charge was levied for (imported entries only).</summary>
    public long? Quantity { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidedBy { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    public static LedgerEntry Create(
        Guid accountId,
        DateOnly entryDate,
        string? voucherNo,
        string? description,
        string? narration,
        string? exchange,
        decimal amount,
        LedgerClass entryClass,
        decimal? runningBalance,
        string externalId,
        Guid runId,
        DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A ledger entry belongs to an account.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > MaxExternalIdLength)
        {
            throw new ArgumentException($"The external id has 1 to {MaxExternalIdLength} characters.", nameof(externalId));
        }

        return new LedgerEntry(
            Guid.CreateVersion7(now),
            accountId,
            entryDate,
            Clean(voucherNo, MaxVoucherNoLength),
            Clean(description, MaxDescriptionLength),
            Clean(narration, MaxNarrationLength),
            exchange is null ? null : Clean(exchange, MaxExchangeLength) is { Length: > 0 } venue ? venue : null,
            Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            entryClass,
            runningBalance is { } balance ? Math.Round(balance, 2, MidpointRounding.AwayFromZero) : null,
            externalId,
            runId,
            now,
            LedgerSource.Broker,
            null,
            null,
            null);
    }

    /// <summary>
    /// A cost the owner imported from a report (ADR 0029), such as a depository charge: class <see cref="LedgerClass.Charges"/>, <paramref name="paid"/> rupees paid (stored as the negative amount), for the security <paramref name="isin"/> and <paramref name="quantity"/>
    /// shares, in the import batch <paramref name="batchId"/>. Not read from Dhan, so it has no run.
    /// </summary>
    public static LedgerEntry FromImport(Guid accountId, DateOnly entryDate, string description, string narration, decimal paid, string? isin, long? quantity, Guid batchId, string externalId, DateTimeOffset now)
    {
        if (accountId == Guid.Empty || batchId == Guid.Empty)
        {
            throw new ArgumentException("An imported entry belongs to an account and a batch.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > MaxExternalIdLength)
        {
            throw new ArgumentException($"The external id has 1 to {MaxExternalIdLength} characters.", nameof(externalId));
        }

        if (paid <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(paid), "An imported cost is above 0.");
        }

        if (quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A quantity is above 0.");
        }

        var code = TradeRules.NormaliseIsin(isin);
        return new LedgerEntry(
            Guid.CreateVersion7(now),
            accountId,
            entryDate,
            string.Empty,
            Clean(description, MaxDescriptionLength),
            Clean(narration, MaxNarrationLength),
            null,
            -Math.Round(paid, 2, MidpointRounding.AwayFromZero),
            LedgerClass.Charges,
            null,
            externalId,
            null,
            now,
            LedgerSource.Import,
            batchId,
            code is null ? null : Clean(code, Holding.MaxIsinLength),
            quantity);
    }

    /// <summary>Voids an imported entry (it stops counting, it is never deleted). False for an entry read from Dhan and for one voided already.</summary>
    public bool Void(string by, DateTimeOffset at)
    {
        if (Source != LedgerSource.Import || VoidedAt is not null)
        {
            return false;
        }

        var actor = by?.Trim();
        VoidedBy = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= Transaction.MaxCreatedByLength ? actor : actor[..Transaction.MaxCreatedByLength];
        VoidedAt = at;
        return true;
    }

    /// <summary>Dhan's text with control characters (line breaks, tabs) turned into spaces, trimmed and cut to <paramref name="max"/> characters.</summary>
    public static string Clean(string? text, int max)
    {
        var clean = new string([.. (text ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
