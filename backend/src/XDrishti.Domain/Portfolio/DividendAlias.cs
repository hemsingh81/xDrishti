using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// What the owner decided a scrip name of a dividend file means for one account (ADR 0028): the normalised name (<see cref="NameKey"/>) points to a holding, to an instrument of the master list, or to nothing (<see cref="DividendAliasTarget.None"/>:
/// keep such dividends unallocated). An alias only guides the next files; dividends imported before are never changed. The owner saves another choice for the same name by retargeting it.
/// </summary>
public sealed class DividendAlias : Entity<Guid>
{
    public const int MaxNameKeyLength = 200;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private DividendAlias(Guid id, Guid accountId, string nameKey, Guid? holdingId, Guid? instrumentId, string createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        AccountId = accountId;
        NameKey = nameKey;
        HoldingId = holdingId;
        InstrumentId = instrumentId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    /// <summary>The scrip name as the matcher compares it (lower case words, punctuation and corporate suffixes gone).</summary>
    public string NameKey { get; private set; }

    /// <summary>The holding the name means; null when it means an instrument or nothing.</summary>
    public Guid? HoldingId { get; private set; }

    /// <summary>The instrument of the master list the name means (a share that is not held); null when it means a holding or nothing.</summary>
    public Guid? InstrumentId { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DividendAliasTarget Target => HoldingId is not null ? DividendAliasTarget.Holding : InstrumentId is not null ? DividendAliasTarget.Instrument : DividendAliasTarget.None;

    public static DividendAlias Create(Guid accountId, string nameKey, Guid? holdingId, Guid? instrumentId, string createdBy, DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An alias belongs to an account.", nameof(accountId));
        }

        var key = Key(nameKey);
        if (holdingId is not null && instrumentId is not null)
        {
            throw new ArgumentException("An alias points to a holding or to an instrument, not both.", nameof(instrumentId));
        }

        return new DividendAlias(Guid.CreateVersion7(now), accountId, key, holdingId, instrumentId, Actor(createdBy), now);
    }

    /// <summary>Points the alias somewhere else. False when it points there already.</summary>
    public bool Retarget(Guid? holdingId, Guid? instrumentId, DateTimeOffset now)
    {
        if (holdingId is not null && instrumentId is not null)
        {
            throw new ArgumentException("An alias points to a holding or to an instrument, not both.", nameof(instrumentId));
        }

        if (HoldingId == holdingId && InstrumentId == instrumentId)
        {
            return false;
        }

        HoldingId = holdingId;
        InstrumentId = instrumentId;
        UpdatedAt = now;
        return true;
    }

    private static string Key(string? nameKey)
    {
        var key = nameKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxNameKeyLength)
        {
            throw new ArgumentException($"The name key is 1 to {MaxNameKeyLength} characters.", nameof(nameKey));
        }

        return key;
    }

    private static string Actor(string? by)
    {
        var actor = by?.Trim();
        return string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= Dividend.MaxCreatedByLength ? actor : actor[..Dividend.MaxCreatedByLength];
    }
}
