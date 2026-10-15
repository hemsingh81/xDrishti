using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One import of a depository-charges sheet (ADR 0029): the file's name and SHA-256 (the file itself is never stored), what was in it (lines read, cost entries stored, lines not stored because they cost nothing, were buy-side, were stored before or are in the
/// ledger already, bad lines), the rupees stored, who imported it and when. Undoing it voids its ledger entries and marks the batch <see cref="CostBatchStatus.Undone"/>. A file that adds nothing new makes no batch.
/// </summary>
public sealed class CostBatch : Entity<Guid>
{
    public const int MaxFileNameLength = 255;

    public const int Sha256Length = 64;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private CostBatch(
        Guid id,
        Guid accountId,
        string fileName,
        string sha256,
        int parsed,
        int imported,
        int duplicates,
        int bad,
        decimal amount,
        CostBatchStatus status,
        string createdBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        AccountId = accountId;
        FileName = fileName;
        Sha256 = sha256;
        Parsed = parsed;
        Imported = imported;
        Duplicates = duplicates;
        Bad = bad;
        Amount = amount;
        Status = status;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    public string FileName { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the file's bytes.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Lines that were valid.</summary>
    public int Parsed { get; private set; }

    /// <summary>Cost entries stored by this import.</summary>
    public int Imported { get; private set; }

    /// <summary>Valid lines that were not stored.</summary>
    public int Duplicates { get; private set; }

    public int Bad { get; private set; }

    /// <summary>Rupees stored by this import (positive: what was paid).</summary>
    public decimal Amount { get; private set; }

    public CostBatchStatus Status { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UndoneAt { get; private set; }

    public static CostBatch Start(Guid id, Guid accountId, string fileName, string sha256, int parsed, int imported, int duplicates, int bad, decimal amount, string createdBy, DateTimeOffset now)
    {
        if (id == Guid.Empty || accountId == Guid.Empty)
        {
            throw new ArgumentException("A batch has an id and belongs to an account.", nameof(accountId));
        }

        if (sha256 is null || sha256.Length != Sha256Length)
        {
            throw new ArgumentException("The SHA-256 is 64 hex characters.", nameof(sha256));
        }

        if (parsed < 0 || imported < 0 || duplicates < 0 || bad < 0 || amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(parsed), "Counts and amounts cannot be negative.");
        }

        var clean = new string([.. (fileName ?? string.Empty).Where(c => !char.IsControl(c))]).Trim();
        var name = clean[(clean.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        name = string.IsNullOrEmpty(name) ? "dp-charges.xls" : name.Length <= MaxFileNameLength ? name : name[..MaxFileNameLength];
        var actor = createdBy?.Trim();
        actor = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= Transaction.MaxCreatedByLength ? actor : actor[..Transaction.MaxCreatedByLength];
        return new CostBatch(id, accountId, name, sha256.ToLowerInvariant(), parsed, imported, duplicates, bad, Math.Round(amount, 2, MidpointRounding.AwayFromZero), CostBatchStatus.Imported, actor, now);
    }

    /// <summary>Marks the batch undone. False when it was undone already.</summary>
    public bool Undo(DateTimeOffset at)
    {
        if (Status == CostBatchStatus.Undone)
        {
            return false;
        }

        Status = CostBatchStatus.Undone;
        UndoneAt = at;
        return true;
    }
}
