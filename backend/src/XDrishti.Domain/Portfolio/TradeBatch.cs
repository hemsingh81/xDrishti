using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One import of a tradebook file (ADR 0023): the file's name and SHA-256 (the file itself is never stored), what was in it (rows parsed, trades imported, trades already imported,
/// rows skipped, rows that were bad), who imported it and when. Undoing it voids its trades and marks the batch <see cref="TradeBatchStatus.Undone"/>. A file that adds nothing new makes no batch.
/// </summary>
public sealed class TradeBatch : Entity<Guid>
{
    public const int MaxFileNameLength = 255;

    public const int Sha256Length = 64;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private TradeBatch(
        Guid id,
        Guid accountId,
        string fileName,
        string sha256,
        int parsed,
        int imported,
        int duplicates,
        int skipped,
        int bad,
        TradeBatchStatus status,
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
        Skipped = skipped;
        Bad = bad;
        Status = status;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    public string FileName { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the file's bytes.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Rows that were valid trades of equities.</summary>
    public int Parsed { get; private set; }

    /// <summary>Trades stored by this import.</summary>
    public int Imported { get; private set; }

    /// <summary>Valid rows that were not stored: repeated in the file or imported before.</summary>
    public int Duplicates { get; private set; }

    public int Skipped { get; private set; }

    public int Bad { get; private set; }

    public TradeBatchStatus Status { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UndoneAt { get; private set; }

    public static TradeBatch Start(Guid id, Guid accountId, string fileName, string sha256, int parsed, int imported, int duplicates, int skipped, int bad, string createdBy, DateTimeOffset now)
    {
        if (id == Guid.Empty || accountId == Guid.Empty)
        {
            throw new ArgumentException("A batch has an id and belongs to an account.", nameof(accountId));
        }

        if (sha256 is null || sha256.Length != Sha256Length)
        {
            throw new ArgumentException("The SHA-256 is 64 hex characters.", nameof(sha256));
        }

        if (parsed < 0 || imported < 0 || duplicates < 0 || skipped < 0 || bad < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parsed), "Counts cannot be negative.");
        }

        var clean = new string([.. (fileName ?? string.Empty).Where(c => !char.IsControl(c))]).Trim();
        var name = clean[(clean.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        name = string.IsNullOrEmpty(name) ? "trades.csv" : name.Length <= MaxFileNameLength ? name : name[..MaxFileNameLength];
        var actor = createdBy?.Trim();
        actor = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= Transaction.MaxCreatedByLength ? actor : actor[..Transaction.MaxCreatedByLength];
        return new TradeBatch(id, accountId, name, sha256.ToLowerInvariant(), parsed, imported, duplicates, skipped, bad, TradeBatchStatus.Imported, actor, now);
    }

    /// <summary>Marks the batch undone. False when it was undone already.</summary>
    public bool Undo(DateTimeOffset at)
    {
        if (Status == TradeBatchStatus.Undone)
        {
            return false;
        }

        Status = TradeBatchStatus.Undone;
        UndoneAt = at;
        return true;
    }
}
