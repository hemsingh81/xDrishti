namespace XDrishti.Domain.Portfolio;

/// <summary>
/// How far the broker history of an account has been read (ADR 0025): <see cref="WatermarkDate"/> is the last India date a window of <see cref="Kind"/> was read and stored up to. The next run starts at that date again (the last day is read twice,
/// which the identities make harmless). One row per account and kind; it only moves forward, and only over
/// a window that starts at or before the day after it (so there is never a hole behind it). The store moves it with one guarded statement inside the window's unit of work (<c>IBrokerSyncStore.MoveWatermarkAsync</c>); the entity is for reading.
/// </summary>
public sealed class BrokerSyncState
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BrokerSyncState(Guid accountId, BrokerSyncKind kind, DateOnly watermarkDate, DateTimeOffset updatedAt)
    {
        AccountId = accountId;
        Kind = kind;
        WatermarkDate = watermarkDate;
        UpdatedAt = updatedAt;
    }

    public Guid AccountId { get; private set; }

    public BrokerSyncKind Kind { get; private set; }

    public DateOnly WatermarkDate { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
