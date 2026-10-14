using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One line of the account's ledger at Dhan (ADR 0025): a voucher on an India date with one signed <see cref="Amount"/> (credit − debit, rupees; positive adds to the cash balance), the class decided from its texts, and Dhan's running balance when it gives
/// one. Immutable and never deleted. <see cref="ExternalId"/> is the entry's identity (a hash of the account, date, voucher number, description, amount and the occurrence index), unique per account, so reading a window twice stores nothing new.
/// <see cref="Description"/> and <see cref="Narration"/> are Dhan's free text: untrusted, length-capped, to be shown as plain text only.
/// </summary>
public sealed class LedgerEntry : Entity<Guid>
{
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
        Guid runId,
        DateTimeOffset createdAt)
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

    public Guid RunId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

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
            now);
    }

    /// <summary>Dhan's text with control characters (line breaks, tabs) turned into spaces, trimmed and cut to <paramref name="max"/> characters.</summary>
    public static string Clean(string? text, int max)
    {
        var clean = new string([.. (text ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
