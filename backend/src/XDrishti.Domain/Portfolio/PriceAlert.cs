using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// A one-shot price alert on a master-list NSE share or ETF (ADR 0022): <see cref="Direction"/> and <see cref="Level"/>, an optional note, and a status that goes from <see cref="AlertStatus.Active"/> to
/// <see cref="AlertStatus.Triggered"/> (by the quotes service, with the trigger price and time) or <see cref="AlertStatus.Cancelled"/>. Never deleted. The change of status is a conditional update in the database
/// (<c>WHERE status = 'active'</c>), so an alert triggers or is cancelled at most once whatever runs overlap.
/// </summary>
public sealed class PriceAlert : Entity<Guid>
{
    /// <summary>The highest level an alert may have.</summary>
    public const decimal MaxLevel = 10_000_000m;

    public const int LevelDecimals = 4;

    public const int MaxNoteLength = 200;

    public const int MaxCreatedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private PriceAlert(
        Guid id, Guid instrumentId, AlertDirection direction, decimal level, string? note, AlertStatus status, DateTimeOffset createdAt, string createdBy, DateTimeOffset? triggeredAt, decimal? triggerPrice)
        : base(id)
    {
        InstrumentId = instrumentId;
        Direction = direction;
        Level = level;
        Note = note;
        Status = status;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        TriggeredAt = triggeredAt;
        TriggerPrice = triggerPrice;
    }

    public Guid InstrumentId { get; private set; }

    public AlertDirection Direction { get; private set; }

    public decimal Level { get; private set; }

    public string? Note { get; private set; }

    public AlertStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset? TriggeredAt { get; private set; }

    public decimal? TriggerPrice { get; private set; }

    /// <summary>True when a level can be an alert's: above zero, at most <see cref="MaxLevel"/> and with at most <see cref="LevelDecimals"/> decimals.</summary>
    public static bool IsValidLevel(decimal level) => level > 0m && level <= MaxLevel && decimal.Round(level, LevelDecimals) == level;

    /// <summary>The condition of an alert: <c>at_or_below</c> holds when the price is at or below the level, <c>at_or_above</c> when it is at or above it (equality counts).</summary>
    public static bool Holds(AlertDirection direction, decimal level, decimal price) => direction switch
    {
        AlertDirection.AtOrBelow => price <= level,
        AlertDirection.AtOrAbove => price >= level,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static PriceAlert Create(Guid instrumentId, AlertDirection direction, decimal level, string? note, string createdBy, DateTimeOffset now)
    {
        if (instrumentId == Guid.Empty)
        {
            throw new ArgumentException("An alert needs an instrument.", nameof(instrumentId));
        }

        if (!IsValidLevel(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), "The level must be above zero, at most 10,000,000, with at most 4 decimals.");
        }

        var text = note?.Trim();
        if (text is { Length: > MaxNoteLength })
        {
            throw new ArgumentException($"The note has at most {MaxNoteLength} characters.", nameof(note));
        }

        var actor = createdBy?.Trim();
        actor = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= MaxCreatedByLength ? actor : actor[..MaxCreatedByLength];
        return new PriceAlert(Guid.CreateVersion7(now), instrumentId, direction, level, string.IsNullOrEmpty(text) ? null : text, AlertStatus.Active, now, actor, null, null);
    }
}
