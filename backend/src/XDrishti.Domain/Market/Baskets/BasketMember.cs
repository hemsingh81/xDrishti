using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Baskets;

/// <summary>
/// One stay of an instrument in a basket, with its dates (docs/design/06 §3.2). Removing sets <see cref="ValidTo"/> and keeps the row;
/// adding the instrument again makes a new row, so the history shows every stay. An instrument is in a basket at most once at a time
/// (the database enforces one row with no <see cref="ValidTo"/> per basket and instrument).
/// </summary>
public sealed class BasketMember : Entity<Guid>
{
    public const int MaxActorLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BasketMember(
        Guid id, Guid basketId, Guid instrumentId, int position, DateTimeOffset validFrom, DateTimeOffset? validTo, string? note, string addedBy, string? removedBy)
        : base(id)
    {
        BasketId = basketId;
        InstrumentId = instrumentId;
        Position = position;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Note = note;
        AddedBy = addedBy;
        RemovedBy = removedBy;
    }

    public Guid BasketId { get; private set; }

    public Guid InstrumentId { get; private set; }

    /// <summary>Place among the active members of the basket (0 first).</summary>
    public int Position { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    /// <summary>When it was removed; null while it is a member.</summary>
    public DateTimeOffset? ValidTo { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Who added it (the signed-in owner's e-mail address).</summary>
    public string AddedBy { get; private set; }

    /// <summary>Who removed it; null while it is a member.</summary>
    public string? RemovedBy { get; private set; }

    public bool IsActive => ValidTo is null;

    /// <summary>A note of up to <see cref="BasketLimits.MaxNoteLength"/> characters without control characters (blank = none).</summary>
    public static Result<string?> CheckNote(string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return trimmed is not null && (trimmed.Length > BasketLimits.MaxNoteLength || trimmed.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            ? Result.Failure<string?>(BasketErrors.Invalid("note", $"Use at most {BasketLimits.MaxNoteLength} characters."))
            : Result.Success(trimmed);
    }

    /// <summary>The instrument becomes a member from <paramref name="now"/>, at <paramref name="position"/> among the active members. <paramref name="note"/> is stored as given (see <see cref="CheckNote"/>).</summary>
    public static BasketMember Add(Guid basketId, Guid instrumentId, int position, string? note, string actor, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), basketId, instrumentId, position, now, null, note, Limit(actor), null);

    /// <summary>Ends the stay; false when it has ended already.</summary>
    public bool Remove(string actor, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        ValidTo = now;
        RemovedBy = Limit(actor);
        return true;
    }

    /// <summary>Puts the member at a place among the active members; false when it is there already.</summary>
    public bool MoveTo(int position)
    {
        if (Position == position)
        {
            return false;
        }

        Position = position;
        return true;
    }

    /// <summary>
    /// Whether the instrument was a member when <paramref name="cutoff"/> came: it had been added before (<c>ValidFrom &lt; cutoff</c>) and not removed
    /// until then or later (<c>ValidTo</c> empty or <c>&gt;= cutoff</c>). With the start of the day after a date as cutoff this is "a member at the end of that day".
    /// </summary>
    public bool WasMemberAt(DateTimeOffset cutoff) => ValidFrom < cutoff && (ValidTo is null || ValidTo >= cutoff);

    /// <summary>The first instant after the given India date (in UTC, as the database takes it): the cutoff for "a member on that date".</summary>
    public static DateTimeOffset CutoffAfter(DateOnly asOf) =>
        new DateTimeOffset(asOf.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();

    private static string Limit(string actor)
    {
        var trimmed = actor?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "unknown" : trimmed.Length <= MaxActorLength ? trimmed : trimmed[..MaxActorLength];
    }
}
