namespace XDrishti.Domain.Portfolio;

/// <summary>
/// How far the broker history of an account has been read (ADR 0025) and how healthy its syncs are (ADR 0027): <see cref="WatermarkDate"/> is the last India date a window of <see cref="Kind"/> was read and stored up to (null while nothing
/// was read yet - the row then only holds the health figures). The next run starts at that date again (the last day is read twice, which the identities make harmless). One row per account and kind; the watermark only moves forward, and only over
/// a window that starts at or before the day after it (so there is never a hole behind it). The store moves it with one guarded statement inside the window's unit of work (<c>IBrokerSyncStore.MoveWatermarkAsync</c>) and keeps the health figures with
/// single guarded statements (<c>ISyncHealthStore</c>); the entity is for reading.
/// <para>
/// Health: <see cref="ConsecutiveFailedDays"/> counts the India days whose last attempt failed (reset by a success), <see cref="LastFailureReason"/> and <see cref="LastFailedAt"/> describe the latest failure, <see cref="Attempts"/> the failed
/// attempts of that day (the retries use it), <see cref="DeepenedTo"/> the earliest date an earlier-history read reached (trades), <see cref="DeepenExhausted"/> that no earlier year is read any more, <see cref="DeepenedOn"/> the India date of
/// the latest such read, <see cref="MapperVersion"/> the version of the Dhan mappers the kind was last read with.
/// </para>
/// </summary>
public sealed class BrokerSyncState
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BrokerSyncState(Guid accountId, SyncHealthKind kind, DateOnly? watermarkDate, DateTimeOffset updatedAt)
    {
        AccountId = accountId;
        Kind = kind;
        WatermarkDate = watermarkDate;
        UpdatedAt = updatedAt;
    }

    public Guid AccountId { get; private set; }

    public SyncHealthKind Kind { get; private set; }

    public DateOnly? WatermarkDate { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int ConsecutiveFailedDays { get; private set; }

    public string? LastFailureReason { get; private set; }

    public DateTimeOffset? LastFailedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateOnly? DeepenedTo { get; private set; }

    public bool DeepenExhausted { get; private set; }

    public DateOnly? DeepenedOn { get; private set; }

    public int MapperVersion { get; private set; }
}
